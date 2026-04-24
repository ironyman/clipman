using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Text;
using System.Text.Json;
using Clipman.Models;
using System.Diagnostics;
using Microsoft.VisualBasic.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace Clipman.Services;

public sealed class ClipboardCaptureService : IClipboardCaptureService
{
    private const int MaxFormatCount = 128;
    private const int MaxFormatNameLength = 96;

    public async Task<ClipboardClip?> CaptureAsync(CancellationToken cancellationToken = default)
    {
        DataPackageView view;
        try
        {
            view = Clipboard.GetContent();
        }
        catch
        {
            return null;
        }

        var formats = SafeGetAvailableFormats(view);
        if (formats.Length == 0)
        {
            return null;
        }

        var copiedAt = DateTimeOffset.Now;
        var sourceContext = TryGetSourceContext();
        var formatsJson = SafeSerializeFormats(formats);
        var fileClip = await TryCreateStorageItemClipAsync(view, formatsJson, copiedAt, sourceContext);
        if (fileClip is not null)
        {
            return ApplySourceContext(fileClip, sourceContext);
        }

        if (view.Contains(StandardDataFormats.Bitmap))
        {
            var imageBytes = await ReadBitmapAsync(view);
            if (imageBytes is not null)
            {
                return BuildClip(
                    ClipKind.Image,
                    "Clipboard image",
                    "Bitmap image captured from clipboard",
                    "Bitmap image",
                    null,
                    imageBytes,
                    null,
                    formatsJson,
                    copiedAt,
                    sourceContext);
            }
        }

        if (view.Contains(StandardDataFormats.Text))
        {
            var text = await view.GetTextAsync().AsTask(cancellationToken);
            var kind = DetectTextKind(text);
            return BuildClip(
                kind,
                TitleFromText(text, kind),
                PreviewText(text),
                LabelForText(kind, text),
                text,
                null,
                null,
                formatsJson,
                copiedAt,
                sourceContext);
        }

        if (view.Contains(StandardDataFormats.Html))
        {
            var html = await view.GetHtmlFormatAsync().AsTask(cancellationToken);
            return BuildClip(
                ClipKind.Html,
                "HTML fragment",
                PreviewText(HtmlFormatHelper.GetStaticFragment(html)),
                "HTML",
                html,
                null,
                null,
                formatsJson,
                copiedAt,
                sourceContext);
        }

        return BuildClip(
            ClipKind.Other,
            "Clipboard data",
            string.Join(", ", formats),
            "Reference formats",
            null,
            null,
            null,
            formatsJson,
            copiedAt,
            sourceContext);
    }

    private static string[] SafeGetAvailableFormats(DataPackageView view)
    {
        try
        {
            var results = new List<string>(16);
            foreach (var rawFormat in view.AvailableFormats)
            {
                if (results.Count >= MaxFormatCount)
                {
                    results.Add("...truncated");
                    break;
                }

                if (string.IsNullOrWhiteSpace(rawFormat))
                {
                    continue;
                }

                var format = rawFormat.Length <= MaxFormatNameLength
                    ? rawFormat
                    : $"{rawFormat[..MaxFormatNameLength]}...";
                results.Add(format);
            }

            return results.ToArray();
        }
        catch (OutOfMemoryException)
        {
            return [];
        }
        catch
        {
            return [];
        }
    }

    private static string SafeSerializeFormats(string[] formats)
    {
        try
        {
            return JsonSerializer.Serialize(formats);
        }
        catch
        {
            return "[]";
        }
    }

    private static async Task<ClipboardClip?> TryCreateStorageItemClipAsync(
        DataPackageView view,
        string formatsJson,
        DateTimeOffset copiedAt,
        SourceContext sourceContext)
    {
        if (!view.Contains(StandardDataFormats.StorageItems))
        {
            return null;
        }

        var items = await view.GetStorageItemsAsync();
        var paths = items
            .Select(item => item is StorageFile file ? file.Path : item.Path)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToArray();

        if (paths.Length == 0)
        {
            return null;
        }

        var firstPath = paths[0];
        var kind = IsVideo(firstPath) ? ClipKind.Video : ClipKind.File;
        var title = paths.Length == 1 ? Path.GetFileName(firstPath) : $"{paths.Length} files";
        return BuildClip(
            kind,
            title,
            string.Join(Environment.NewLine, paths),
            paths.Length == 1 ? "File reference" : "File references",
            null,
            null,
            string.Join(Environment.NewLine, paths),
            formatsJson,
            copiedAt,
            sourceContext);
    }

    private static async Task<byte[]?> ReadBitmapAsync(DataPackageView view)
    {
        var reference = await view.GetBitmapAsync();
        await using var stream = (await reference.OpenReadAsync()).AsStreamForRead();
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory);
        return memory.ToArray();
    }

    private static ClipboardClip BuildClip(
        ClipKind kind,
        string title,
        string preview,
        string formatLabel,
        string? contentText,
        byte[]? contentBytes,
        string? referencePath,
        string formatsJson,
        DateTimeOffset copiedAt,
        SourceContext sourceContext)
    {
        var pageUrl = sourceContext.PageUrl;

        var domain = sourceContext.Domain;
        if (string.IsNullOrWhiteSpace(domain) && Uri.TryCreate(pageUrl, UriKind.Absolute, out var uri))
        {
            domain = uri.Host;
        }

        var hashInput = $"{kind}|{contentText}|{referencePath}|{preview}|{Convert.ToBase64String(SHA256.HashData(contentBytes ?? []))}";
        var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(hashInput)));
        return new ClipboardClip
        {
            Id = id,
            Kind = kind,
            Title = title,
            Preview = preview,
            ContentText = contentText,
            ContentBytes = contentBytes,
            ReferencePath = referencePath,
            FormatsJson = formatsJson,
            FormatLabel = formatLabel,
            SourceApp = sourceContext.AppName,
            SourceWindowTitle = sourceContext.WindowTitle,
            BrowserTabTitle = sourceContext.TabTitle,
            SourceUrl = pageUrl,
            SourceDomain = domain,
            CopiedAt = copiedAt
        };
    }

    private static ClipboardClip ApplySourceContext(ClipboardClip clip, SourceContext sourceContext)
    {
        var pageUrl = sourceContext.PageUrl ?? clip.SourceUrl;

        var domain = sourceContext.Domain;
        if (string.IsNullOrWhiteSpace(domain) && Uri.TryCreate(pageUrl, UriKind.Absolute, out var uri))
        {
            domain = uri.Host;
        }

        return new ClipboardClip
        {
            Id = clip.Id,
            Kind = clip.Kind,
            Title = clip.Title,
            Preview = clip.Preview,
            ContentText = clip.ContentText,
            ContentBytes = clip.ContentBytes,
            ReferencePath = clip.ReferencePath,
            FormatsJson = clip.FormatsJson,
            SourceApp = sourceContext.AppName ?? clip.SourceApp,
            SourceWindowTitle = sourceContext.WindowTitle ?? clip.SourceWindowTitle,
            BrowserTabTitle = sourceContext.TabTitle ?? clip.BrowserTabTitle,
            SourceUrl = pageUrl,
            SourceDomain = domain ?? clip.SourceDomain,
            Tags = clip.Tags,
            FormatLabel = clip.FormatLabel,
            CopiedAt = clip.CopiedAt,
            IsPinned = clip.IsPinned,
            UseCount = clip.UseCount,
            AccentHex = clip.AccentHex
        };
    }

    private static string? ExtractUrl(string text)
    {
        var trimmed = text.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            return uri.ToString();
        }

        var match = Regex.Match(trimmed, @"https?://[^\s]+", RegexOptions.IgnoreCase);
        return match.Success ? match.Value : null;
    }

    private static ClipKind DetectTextKind(string text)
    {
        if (Uri.TryCreate(text.Trim(), UriKind.Absolute, out _))
        {
            return ClipKind.Url;
        }

        var trimmed = text.TrimStart();
        if (trimmed.StartsWith("using ", StringComparison.Ordinal) ||
            trimmed.StartsWith("const ", StringComparison.Ordinal) ||
            trimmed.StartsWith("function ", StringComparison.Ordinal) ||
            trimmed.Contains("=>", StringComparison.Ordinal) ||
            trimmed.Contains("{", StringComparison.Ordinal) && trimmed.Contains(";", StringComparison.Ordinal))
        {
            return ClipKind.Code;
        }

        return ClipKind.Text;
    }

    private static string TitleFromText(string text, ClipKind kind)
    {
        var firstLine = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
        if (string.IsNullOrWhiteSpace(firstLine))
        {
            return kind == ClipKind.Url ? "URL" : "Text";
        }

        return firstLine.Length <= 80 ? firstLine : $"{firstLine[..77]}...";
    }

    private static string PreviewText(string text)
    {
        var compact = string.Join(" ", text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Select(line => line.Trim()));
        return compact.Length <= 240 ? compact : $"{compact[..237]}...";
    }

    private static string LabelForText(ClipKind kind, string text) =>
        kind switch
        {
            ClipKind.Url => "URL",
            ClipKind.Code => $"Code - {text.Split('\n').Length} lines",
            _ => "Plain text"
        };

    private static bool IsVideo(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        return extension is ".mp4" or ".mov" or ".mkv" or ".avi" or ".webm" or ".wmv";
    }

    private static SourceContext TryGetSourceContext()
    {
        try
        {
            var windowHandle = GetForegroundWindow();
            if (windowHandle == IntPtr.Zero)
            {
                return SourceContext.Empty;
            }

            var titleBuilder = new StringBuilder(512);
            _ = GetWindowText(windowHandle, titleBuilder, titleBuilder.Capacity);
            var windowTitle = titleBuilder.ToString().Trim();

            _ = GetWindowThreadProcessId(windowHandle, out var processId);
            if (processId == 0)
            {
                return new SourceContext(null, windowTitle, null, ExtractUrl(windowTitle), null);
            }

            var process = Process.GetProcessById((int)processId);
            var processName = process.ProcessName;
            var appName = GetFriendlyAppName(processName);

            var isBrowser = IsBrowser(processName);
            var tabTitle = isBrowser ? ExtractBrowserTabTitle(windowTitle, appName) : null;
            var pageUrl = isBrowser ? TryGetBrowserUrl(windowHandle, processName, windowTitle) : null;
            var domain = Uri.TryCreate(pageUrl, UriKind.Absolute, out var uri) ? uri.Host : null;

            return new SourceContext(appName, windowTitle, tabTitle, pageUrl, domain);
        }
        catch
        {
            return SourceContext.Empty;
        }
    }

    private static bool IsBrowser(string processName) =>
        processName.Equals("msedge", StringComparison.OrdinalIgnoreCase) ||
        processName.Equals("chrome", StringComparison.OrdinalIgnoreCase) ||
        processName.Equals("firefox", StringComparison.OrdinalIgnoreCase) ||
        processName.Equals("brave", StringComparison.OrdinalIgnoreCase);

    /*
    C++ sample requested:
    #include <uiautomation.h>

    IUIAutomation* automation = nullptr;
    CoCreateInstance(CLSID_CUIAutomation, nullptr, CLSCTX_INPROC_SERVER,
                     IID_IUIAutomation, (void**)&automation);

    // Get Edge window element
    IUIAutomationElement* root = nullptr;
    automation->ElementFromHandle(hwndEdge, &root);

    // Find address bar (Edit control)
    IUIAutomationCondition* cond = nullptr;
    automation->CreatePropertyCondition(UIA_ControlTypePropertyId,
        _variant_t(UIA_EditControlTypeId), &cond);

    IUIAutomationElement* addressBar = nullptr;
    root->FindFirst(TreeScope_Subtree, cond, &addressBar);

    // Get value
    IUIAutomationValuePattern* valuePattern = nullptr;
    addressBar->GetCurrentPatternAs(UIA_ValuePatternId,
        IID_IUIAutomationValuePattern, (void**)&valuePattern);

    BSTR url;
    valuePattern->get_CurrentValue(&url);
    */
    private static string? TryGetBrowserUrl(IntPtr windowHandle, string processName, string windowTitle)
    {
        if (processName.Equals("msedge", StringComparison.OrdinalIgnoreCase))
        {
            var url = TryGetEdgeUrlFromUiAutomation(windowHandle);
            if (!string.IsNullOrWhiteSpace(url))
            {
                return url;
            }
        }

        return ExtractUrl(windowTitle);
    }

    private static string? TryGetEdgeUrlFromUiAutomation(IntPtr edgeWindowHandle)
    {
        var nativeUrl = TryGetEdgeUrlFromNativeBridge(edgeWindowHandle);
        if (!string.IsNullOrWhiteSpace(nativeUrl))
        {
            return nativeUrl;
        }

        try
        {
            var automation = CreateUiAutomationInstance();
            if (automation is null)
            {
                return null;
            }

            var root = InvokeComMethod(automation, "ElementFromHandle", edgeWindowHandle);
            if (root is null)
            {
                return null;
            }

            const int uiaControlTypePropertyId = 30003;
            const int uiaEditControlTypeId = 50004;
            const int uiaNamePropertyId = 30005;
            const int treeScopeSubtree = 7;
            const int uiaValuePatternId = 10002;

            var condition = InvokeComMethod(automation, "CreatePropertyCondition", uiaControlTypePropertyId, uiaEditControlTypeId);
            if (condition is null)
            {
                return null;
            }

            var edits = InvokeComMethod(root, "FindAll", treeScopeSubtree, condition);
            if (edits is null)
            {
                return null;
            }

            var count = Convert.ToInt32(InvokeComPropertyGet(edits, "Length") ?? 0);
            string? bestCandidate = null;

            for (var i = 0; i < count; i++)
            {
                var element = InvokeComMethod(edits, "GetElement", i);
                if (element is null)
                {
                    continue;
                }

                var valuePattern = InvokeComMethod(element, "GetCurrentPattern", uiaValuePatternId);
                if (valuePattern is null)
                {
                    continue;
                }

                string? value = (InvokeComPropertyGet(valuePattern, "CurrentValue") as string)?.Trim();
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                string? normalizedUrl = NormalizeUrlCandidate(value);
                if (normalizedUrl is null)
                {
                    continue;
                }

                var name = (InvokeComMethod(element, "GetCurrentPropertyValue", uiaNamePropertyId) as string)?.Trim();
                var isAddressBar = name?.Contains("address and search", StringComparison.OrdinalIgnoreCase) == true ||
                                   name?.Contains("address bar", StringComparison.OrdinalIgnoreCase) == true;

                if (isAddressBar)
                {
                    return normalizedUrl;
                }

                bestCandidate ??= normalizedUrl;
            }

            return bestCandidate;
        }
        catch
        {
        }

        return null;
    }

    private static string? TryGetEdgeUrlFromNativeBridge(IntPtr edgeWindowHandle)
    {
        try
        {
            var buffer = new StringBuilder(2048);
            var chars = GetEdgeUrlFromWindow(edgeWindowHandle, buffer, buffer.Capacity);
            if (chars > 0)
            {
                var value = buffer.ToString().Trim();
                return NormalizeUrlCandidate(value);
            }
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }
        catch
        {
        }

        return null;
    }

    private static string? NormalizeUrlCandidate(string value)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var absolute))
        {
            return absolute.ToString();
        }

        if (value.Contains(' ') || !value.Contains('.'))
        {
            return null;
        }

        var tentative = $"https://{value.TrimStart('/')}";
        return Uri.TryCreate(tentative, UriKind.Absolute, out var hostUri) ? hostUri.ToString() : null;
    }

    private static object? CreateUiAutomationInstance()
    {
        var candidateProgIds = new[]
        {
            "UIAutomationClient.CUIAutomation8",
            "UIAutomationClient.CUIAutomation"
        };

        foreach (var progId in candidateProgIds)
        {
            try
            {
                var type = Type.GetTypeFromProgID(progId, throwOnError: false);
                if (type is not null)
                {
                    var instance = Activator.CreateInstance(type);
                    if (instance is not null)
                    {
                        return instance;
                    }
                }
            }
            catch
            {
            }
        }

        var candidateClsids = new[]
        {
            new Guid("E22AD333-B25F-460C-83D0-0581107395C9"),
            new Guid("FF48DBA4-60EF-4201-AA87-54103EEF594E")
        };

        foreach (var clsid in candidateClsids)
        {
            try
            {
                var type = Type.GetTypeFromCLSID(clsid, throwOnError: false);
                if (type is not null)
                {
                    var instance = Activator.CreateInstance(type);
                    if (instance is not null)
                    {
                        return instance;
                    }
                }
            }
            catch
            {
            }
        }

        return null;
    }

    private static object? InvokeComMethod(object target, string methodName, params object[] args)
    {
        try
        {
            var callArgs = args.Cast<object?>().ToArray();
            var copyBack = new bool[callArgs.Length];
            return NewLateBinding.LateCall(
                target,
                null,
                methodName,
                callArgs,
                null,
                null,
                copyBack,
                false);
        }
        catch
        {
            return null;
        }
    }

    private static object? InvokeComPropertyGet(object target, string propertyName)
    {
        try
        {
            return NewLateBinding.LateGet(
                target,
                null,
                propertyName,
                Array.Empty<object>(),
                null,
                null,
                null);
        }
        catch
        {
            return null;
        }
    }

    private static string GetFriendlyAppName(string processName) =>
        processName.ToLowerInvariant() switch
        {
            "msedge" => "Microsoft Edge",
            "chrome" => "Google Chrome",
            "firefox" => "Mozilla Firefox",
            "brave" => "Brave",
            _ => processName
        };

    private static string? ExtractBrowserTabTitle(string windowTitle, string appName)
    {
        if (string.IsNullOrWhiteSpace(windowTitle))
        {
            return null;
        }

        var suffixes = new[]
        {
            $" - {appName}",
            $" — {appName}"
        };

        foreach (var suffix in suffixes)
        {
            if (windowTitle.EndsWith(suffix, StringComparison.Ordinal))
            {
                return windowTitle[..^suffix.Length].Trim();
            }
        }

        return windowTitle;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [SupportedOSPlatform("windows")]
    [DllImport("clipman_uia_bridge.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
    private static extern int GetEdgeUrlFromWindow(IntPtr hwndEdge, StringBuilder output, int outputChars);

    private sealed record SourceContext(
        string? AppName,
        string? WindowTitle,
        string? TabTitle,
        string? PageUrl,
        string? Domain)
    {
        public static SourceContext Empty { get; } = new(null, null, null, null, null);
    }
}
