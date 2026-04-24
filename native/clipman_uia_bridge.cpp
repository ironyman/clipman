#include <windows.h>
#include <ole2.h>
#include <oleauto.h>
#include <uiautomation.h>

#include "clipman_uia_bridge_exports.h"

#include <string>
#include <vector>

static bool ContainsInsensitive(const std::wstring& haystack, const wchar_t* needle)
{
    if (needle == nullptr || *needle == L'\0')
    {
        return false;
    }

    std::wstring h = haystack;
    std::wstring n = needle;
    for (auto& c : h) c = static_cast<wchar_t>(towlower(c));
    for (auto& c : n) c = static_cast<wchar_t>(towlower(c));
    return h.find(n) != std::wstring::npos;
}

static bool IsLikelyUrl(const std::wstring& text)
{
    if (text.empty())
    {
        return false;
    }

    if (ContainsInsensitive(text, L"http://") || ContainsInsensitive(text, L"https://"))
    {
        return true;
    }

    return text.find(L'.') != std::wstring::npos && text.find(L' ') == std::wstring::npos;
}

extern "C" __declspec(dllexport) int __stdcall SendCtrlVToForeground()
{
    constexpr BYTE vkControl = VK_CONTROL;
    constexpr BYTE vkLeftControl = VK_LCONTROL;
    constexpr BYTE vkV = 'V';
    constexpr UINT keyEventfKeyUp = KEYEVENTF_KEYUP;

    const HWND foreground = ::GetForegroundWindow();
    if (foreground == nullptr)
    {
        return 0;
    }

    const BYTE controlScan = static_cast<BYTE>(::MapVirtualKeyW(vkControl, MAPVK_VK_TO_VSC));
    const BYTE leftControlScan = static_cast<BYTE>(::MapVirtualKeyW(vkLeftControl, MAPVK_VK_TO_VSC));
    const BYTE vScan = static_cast<BYTE>(::MapVirtualKeyW(vkV, MAPVK_VK_TO_VSC));

    auto isDown = [](int vk)
    {
        return (::GetAsyncKeyState(vk) & 0x8000) != 0;
    };
    auto releaseKey = [&](BYTE vk)
    {
        if (!isDown(vk))
        {
            return;
        }

        const BYTE scan = static_cast<BYTE>(::MapVirtualKeyW(vk, MAPVK_VK_TO_VSC));
        ::keybd_event(vk, scan, KEYEVENTF_KEYUP, 0);
    };

    // Avoid menu beeps from Alt-triggered hotkeys by waiting for non-paste modifiers to be released.
    for (int i = 0; i < 24; ++i)
    {
        const bool altDown = isDown(VK_MENU) || isDown(VK_LMENU) || isDown(VK_RMENU);
        const bool shiftDown = isDown(VK_SHIFT) || isDown(VK_LSHIFT) || isDown(VK_RSHIFT);
        const bool winDown = isDown(VK_LWIN) || isDown(VK_RWIN);
        if (!altDown && !shiftDown && !winDown)
        {
            break;
        }

        ::Sleep(5);
    }

    // Force-release non-paste modifiers in case the originating hotkey left one logically down.
    releaseKey(VK_MENU);
    releaseKey(VK_LMENU);
    releaseKey(VK_RMENU);
    releaseKey(VK_SHIFT);
    releaseKey(VK_LSHIFT);
    releaseKey(VK_RSHIFT);
    releaseKey(VK_LWIN);
    releaseKey(VK_RWIN);
    ::Sleep(8);

    // keybd_event can drop modifiers if focus/state changes between events.
    // Press left-control explicitly and verify state before sending V.
    ::keybd_event(vkLeftControl, leftControlScan, 0, 0);
    ::Sleep(8);
    if ((::GetAsyncKeyState(VK_CONTROL) & 0x8000) == 0)
    {
        ::keybd_event(vkControl, controlScan, 0, 0);
        ::Sleep(8);
    }

    ::keybd_event(vkV, vScan, 0, 0);
    ::Sleep(6);
    ::keybd_event(vkV, vScan, keyEventfKeyUp, 0);
    ::Sleep(4);
    ::keybd_event(vkLeftControl, leftControlScan, keyEventfKeyUp, 0);
    ::keybd_event(vkControl, controlScan, keyEventfKeyUp, 0);

    return 1;
}

extern "C" __declspec(dllexport) int __stdcall GetEdgeUrlFromWindow(HWND hwndEdge, wchar_t* output, int outputChars)
{
    if (output == nullptr || outputChars <= 1 || hwndEdge == nullptr)
    {
        return 0;
    }

    output[0] = L'\0';

    const HRESULT initHr = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    const bool uninitRequired = SUCCEEDED(initHr);

    IUIAutomation* automation = nullptr;
    HRESULT hr = CoCreateInstance(CLSID_CUIAutomation8, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&automation));
    if (FAILED(hr))
    {
        hr = CoCreateInstance(CLSID_CUIAutomation, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&automation));
    }
    if (FAILED(hr) || automation == nullptr)
    {
        if (uninitRequired) CoUninitialize();
        return 0;
    }

    IUIAutomationElement* root = nullptr;
    hr = automation->ElementFromHandle(hwndEdge, &root);
    if (FAILED(hr) || root == nullptr)
    {
        automation->Release();
        if (uninitRequired) CoUninitialize();
        return 0;
    }

    VARIANT controlType;
    VariantInit(&controlType);
    controlType.vt = VT_I4;
    controlType.lVal = UIA_EditControlTypeId;

    IUIAutomationCondition* condition = nullptr;
    hr = automation->CreatePropertyCondition(UIA_ControlTypePropertyId, controlType, &condition);
    VariantClear(&controlType);
    if (FAILED(hr) || condition == nullptr)
    {
        root->Release();
        automation->Release();
        if (uninitRequired) CoUninitialize();
        return 0;
    }

    IUIAutomationElementArray* edits = nullptr;
    hr = root->FindAll(TreeScope_Subtree, condition, &edits);
    condition->Release();
    if (FAILED(hr) || edits == nullptr)
    {
        root->Release();
        automation->Release();
        if (uninitRequired) CoUninitialize();
        return 0;
    }

    int length = 0;
    edits->get_Length(&length);
    std::wstring bestCandidate;

    for (int i = 0; i < length; ++i)
    {
        IUIAutomationElement* element = nullptr;
        if (FAILED(edits->GetElement(i, &element)) || element == nullptr)
        {
            continue;
        }

        IUIAutomationValuePattern* valuePattern = nullptr;
        hr = element->GetCurrentPatternAs(UIA_ValuePatternId, IID_PPV_ARGS(&valuePattern));
        if (SUCCEEDED(hr) && valuePattern != nullptr)
        {
            BSTR valueBstr = nullptr;
            if (SUCCEEDED(valuePattern->get_CurrentValue(&valueBstr)) && valueBstr != nullptr)
            {
                const std::wstring value(valueBstr, SysStringLen(valueBstr));
                SysFreeString(valueBstr);

                if (IsLikelyUrl(value))
                {
                    BSTR nameBstr = nullptr;
                    std::wstring name;
                    if (SUCCEEDED(element->get_CurrentName(&nameBstr)) && nameBstr != nullptr)
                    {
                        name.assign(nameBstr, SysStringLen(nameBstr));
                        SysFreeString(nameBstr);
                    }

                    const bool isAddressBar = ContainsInsensitive(name, L"address and search") ||
                                              ContainsInsensitive(name, L"address bar");
                    if (isAddressBar)
                    {
                        wcsncpy_s(output, outputChars, value.c_str(), _TRUNCATE);
                        valuePattern->Release();
                        element->Release();
                        edits->Release();
                        root->Release();
                        automation->Release();
                        if (uninitRequired) CoUninitialize();
                        return static_cast<int>(wcslen(output));
                    }

                    if (bestCandidate.empty())
                    {
                        bestCandidate = value;
                    }
                }
            }

            valuePattern->Release();
        }

        element->Release();
    }

    if (!bestCandidate.empty())
    {
        wcsncpy_s(output, outputChars, bestCandidate.c_str(), _TRUNCATE);
    }

    edits->Release();
    root->Release();
    automation->Release();
    if (uninitRequired) CoUninitialize();
    return static_cast<int>(wcslen(output));
}
