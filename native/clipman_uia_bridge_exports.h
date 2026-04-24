#pragma once

#include <windows.h>

extern "C" __declspec(dllexport) int __stdcall GetEdgeUrlFromWindow(HWND hwndEdge, wchar_t* output, int outputChars);
extern "C" __declspec(dllexport) int __stdcall GetFocusCaretScreenPoint(long* x, long* y);
