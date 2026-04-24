#include <windows.h>
#include <ole2.h>
#include <oleauto.h>
#include <oleacc.h>

#include "clipman_uia_bridge_exports.h"

class CaretLocator
{
public:
    using AccessibleObjectFromWindowFn = HRESULT(__stdcall*)(HWND hwnd, DWORD dwId, REFIID riid, void** ppvObject);

    CaretLocator()
        : m_activeWnd(nullptr), m_focusWnd(nullptr), m_oleAccModule(nullptr), m_accessibleObjectFromWindow(nullptr)
    {
        m_oleAccModule = ::LoadLibraryW(L"oleacc.dll");
        if (m_oleAccModule)
        {
            m_accessibleObjectFromWindow = reinterpret_cast<AccessibleObjectFromWindowFn>(
                ::GetProcAddress(m_oleAccModule, "AccessibleObjectFromWindow"));
        }
    }

    ~CaretLocator()
    {
        if (m_oleAccModule)
        {
            ::FreeLibrary(m_oleAccModule);
            m_oleAccModule = nullptr;
        }
    }

    bool TrackActiveWindow()
    {
        HWND newActive = ::GetForegroundWindow();
        if (!::IsWindow(newActive))
        {
            return false;
        }

        HWND newFocus = nullptr;
        DWORD otherThreadId = ::GetWindowThreadProcessId(newActive, nullptr);
        if (otherThreadId != 0)
        {
            GUITHREADINFO guiThreadInfo = {};
            guiThreadInfo.cbSize = sizeof(GUITHREADINFO);
            if (::GetGUIThreadInfo(otherThreadId, &guiThreadInfo))
            {
                newFocus = guiThreadInfo.hwndFocus;
            }
        }

        if (!::IsWindow(newFocus))
        {
            newFocus = newActive;
        }

        if (!::IsWindow(newFocus))
        {
            return false;
        }

        m_activeWnd = newActive;
        m_focusWnd = newFocus;
        return true;
    }

    POINT FocusCaret()
    {
        POINT invalid{ -1, -1 };
        if (!::IsWindow(m_activeWnd) || !::IsWindow(m_focusWnd))
        {
            return invalid;
        }

        POINT pt = TryAccessibleCaret();
        if (IsValidPoint(pt))
        {
            return pt;
        }

        pt = TryGuiThreadInfoCaret();
        if (IsValidPoint(pt))
        {
            return pt;
        }

        pt = TryGetCaretPos();
        if (IsValidPoint(pt))
        {
            return pt;
        }

        return invalid;
    }

private:
    static bool IsValidPoint(const POINT& pt)
    {
        return pt.x >= 0 && pt.y >= 0;
    }

    POINT TryAccessibleCaret()
    {
        POINT invalid{ -1, -1 };
        if (!m_accessibleObjectFromWindow)
        {
            return invalid;
        }

        IAccessible* pAccessible = nullptr;
        HRESULT hr = m_accessibleObjectFromWindow(m_activeWnd, OBJID_CARET, __uuidof(IAccessible), reinterpret_cast<void**>(&pAccessible));
        if (hr != S_OK || pAccessible == nullptr)
        {
            return invalid;
        }

        long left = 0;
        long top = 0;
        long width = 0;
        long height = 0;

        VARIANT varCaret;
        ::VariantInit(&varCaret);
        varCaret.vt = VT_I4;
        varCaret.lVal = CHILDID_SELF;

        hr = pAccessible->accLocation(&left, &top, &width, &height, varCaret);
        pAccessible->Release();

        if (hr == S_OK && left != 0 && top != 0)
        {
            POINT pt{ static_cast<LONG>(left + width), static_cast<LONG>(top + 20) };
            return pt;
        }

        return invalid;
    }

    POINT TryGuiThreadInfoCaret()
    {
        POINT invalid{ -1, -1 };
        DWORD otherThreadId = ::GetWindowThreadProcessId(m_activeWnd, nullptr);
        if (otherThreadId == 0)
        {
            return invalid;
        }

        GUITHREADINFO guiThreadInfo = {};
        guiThreadInfo.cbSize = sizeof(GUITHREADINFO);
        if (!::GetGUIThreadInfo(otherThreadId, &guiThreadInfo))
        {
            return invalid;
        }

        RECT rc = guiThreadInfo.rcCaret;
        if (::IsRectEmpty(&rc))
        {
            return invalid;
        }

        POINT pt{ rc.right, rc.bottom };
        if (!::ClientToScreen(m_focusWnd, &pt))
        {
            return invalid;
        }

        return pt;
    }

    POINT TryGetCaretPos()
    {
        POINT invalid{ -1, -1 };
        DWORD otherThreadId = ::GetWindowThreadProcessId(m_activeWnd, nullptr);
        DWORD currentThreadId = ::GetCurrentThreadId();
        if (otherThreadId == 0)
        {
            return invalid;
        }

        if (!::AttachThreadInput(otherThreadId, currentThreadId, TRUE))
        {
            return invalid;
        }

        POINT pt{ 0, 0 };
        BOOL ok = ::GetCaretPos(&pt);
        ::AttachThreadInput(otherThreadId, currentThreadId, FALSE);

        if (!ok || (pt.x == 0 && pt.y == 0))
        {
            return invalid;
        }

        if (!::ClientToScreen(m_focusWnd, &pt))
        {
            return invalid;
        }

        if (pt.x == 0 && pt.y == 0)
        {
            return invalid;
        }

        pt.y += 20;
        return pt;
    }

    HWND m_activeWnd;
    HWND m_focusWnd;
    HMODULE m_oleAccModule;
    AccessibleObjectFromWindowFn m_accessibleObjectFromWindow;
};

extern "C" __declspec(dllexport) int __stdcall GetFocusCaretScreenPoint(long* x, long* y)
{
    if (x == nullptr || y == nullptr)
    {
        return 0;
    }

    *x = -1;
    *y = -1;

    const HRESULT initHr = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    const bool uninitRequired = SUCCEEDED(initHr);

    CaretLocator locator;
    if (!locator.TrackActiveWindow())
    {
        if (uninitRequired) CoUninitialize();
        return 0;
    }

    const POINT pt = locator.FocusCaret();
    if (pt.x < 0 || pt.y < 0)
    {
        if (uninitRequired) CoUninitialize();
        return 0;
    }

    *x = pt.x;
    *y = pt.y;

    if (uninitRequired) CoUninitialize();
    return 1;
}
