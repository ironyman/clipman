using System.Text;
using Clipman.Models;
using Microsoft.Isam.Esent.Interop;

namespace Clipman.Services;

public sealed class EsentClipboardHistoryService : IClipboardClipRepository, IDisposable
{
    private const string TableName = "Clips";
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Instance _instance;
    private readonly string _databasePath;
    private bool _disposed;

    public EsentClipboardHistoryService()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Clipman",
            "Esent");
        Directory.CreateDirectory(root);

        _databasePath = Path.Combine(root, "history.edb");
        _instance = new Instance("ClipmanHistory")
        {
            Parameters =
            {
                SystemDirectory = root,
                LogFileDirectory = root,
                TempDirectory = root,
                CreatePathIfNotExist = true,
                CircularLog = true
            }
        };

        _instance.Init();
        EnsureDatabase();
    }

    public async Task<IReadOnlyList<ClipboardClip>> GetPageAsync(
        int skip,
        int take,
        string? query = null,
        ClipKind? kind = null,
        bool pinnedOnly = false,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return ReadPage(skip, take, query, kind, pinnedOnly);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var session = new Session(_instance);
            var dbid = OpenDatabase(session);
            using var table = new Table(session, dbid, TableName, OpenTableGrbit.ReadOnly);
            return CountRows(session, table);
        }
        catch
        {
            return 0;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _instance.Dispose();
        _gate.Dispose();
    }

    private void EnsureDatabase()
    {
        using var session = new Session(_instance);
        JET_DBID dbid;

        if (File.Exists(_databasePath))
        {
            Api.JetAttachDatabase(session, _databasePath, AttachDatabaseGrbit.None);
            Api.JetOpenDatabase(session, _databasePath, null, out dbid, OpenDatabaseGrbit.None);
        }
        else
        {
            Api.JetCreateDatabase(session, _databasePath, null, out dbid, CreateDatabaseGrbit.None);
        }

        try
        {
            Api.JetCreateTable(session, dbid, TableName, 16, 100, out var table);
            try
            {
                AddColumn(session, table, "Id", JET_coltyp.LongText);
                AddColumn(session, table, "Kind", JET_coltyp.LongText);
                AddColumn(session, table, "Title", JET_coltyp.LongText);
                AddColumn(session, table, "Preview", JET_coltyp.LongText);
                AddColumn(session, table, "ContentText", JET_coltyp.LongText);
                AddColumn(session, table, "ContentBytes", JET_coltyp.LongBinary);
                AddColumn(session, table, "ReferencePath", JET_coltyp.LongText);
                AddColumn(session, table, "FormatsJson", JET_coltyp.LongText);
                AddColumn(session, table, "SourceApp", JET_coltyp.LongText);
                AddColumn(session, table, "AppIconKey", JET_coltyp.LongText);
                AddColumn(session, table, "SourceWindowTitle", JET_coltyp.LongText);
                AddColumn(session, table, "BrowserTabTitle", JET_coltyp.LongText);
                AddColumn(session, table, "SourceUrl", JET_coltyp.LongText);
                AddColumn(session, table, "SourceDomain", JET_coltyp.LongText);
                AddColumn(session, table, "Tags", JET_coltyp.LongText);
                AddColumn(session, table, "FormatLabel", JET_coltyp.LongText);
                AddColumn(session, table, "CopiedAt", JET_coltyp.Currency);
                AddColumn(session, table, "IsPinned", JET_coltyp.Bit);
                AddColumn(session, table, "UseCount", JET_coltyp.Long);

                Api.JetCreateIndex(session, table, "primary", CreateIndexGrbit.IndexPrimary, "+Id\0\0", 5, 100);
                Api.JetCreateIndex(session, table, "copiedAt", CreateIndexGrbit.None, "-CopiedAt\0\0", 11, 100);
            }
            finally
            {
                Api.JetCloseTable(session, table);
            }
        }
        catch (EsentTableDuplicateException)
        {
            EnsureSchemaColumns(session, dbid);
        }
    }

    private static void EnsureSchemaColumns(Session session, JET_DBID dbid)
    {
        using var table = new Table(session, dbid, TableName, OpenTableGrbit.None);
        AddColumnIfMissing(session, table, "SourceWindowTitle", JET_coltyp.LongText);
        AddColumnIfMissing(session, table, "AppIconKey", JET_coltyp.LongText);
        AddColumnIfMissing(session, table, "BrowserTabTitle", JET_coltyp.LongText);
        AddColumnIfMissing(session, table, "SourceUrl", JET_coltyp.LongText);
        AddColumnIfMissing(session, table, "SourceDomain", JET_coltyp.LongText);
        AddColumnIfMissing(session, table, "Tags", JET_coltyp.LongText);
    }

    private static void AddColumn(Session session, JET_TABLEID table, string name, JET_coltyp type)
    {
        var definition = new JET_COLUMNDEF
        {
            coltyp = type,
            cp = JET_CP.Unicode
        };
        Api.JetAddColumn(session, table, name, definition, null, 0, out _);
    }

    private static void AddColumnIfMissing(Session session, JET_TABLEID table, string name, JET_coltyp type)
    {
        try
        {
            AddColumn(session, table, name, type);
        }
        catch (EsentColumnDuplicateException)
        {
        }
    }

    private JET_DBID OpenDatabase(Session session)
    {
        Api.JetOpenDatabase(session, _databasePath, null, out var dbid, OpenDatabaseGrbit.None);
        return dbid;
    }

    private IReadOnlyList<ClipboardClip> ReadPage(int skip, int take, string? query, ClipKind? kind, bool pinnedOnly)
    {
        using var session = new Session(_instance);
        var dbid = OpenDatabase(session);
        using var table = new Table(session, dbid, TableName, OpenTableGrbit.ReadOnly);
        var columns = Columns(session, table);
        var clips = new List<ClipboardClip>(take);
        var skipped = 0;
        var parsedQuery = ParseSearchQuery(query);

        Api.JetSetCurrentIndex(session, table, "copiedAt");
        if (!Api.TryMoveFirst(session, table))
        {
            return clips;
        }

        do
        {
            var clip = ReadClip(session, table, columns);
            if (Matches(clip, parsedQuery, kind, pinnedOnly))
            {
                if (skipped++ >= skip)
                {
                    clips.Add(clip);
                    if (clips.Count >= take)
                    {
                        break;
                    }
                }
            }
        }
        while (Api.TryMoveNext(session, table));

        return clips;
    }

    private static bool Matches(ClipboardClip clip, SearchQuery query, ClipKind? kind, bool pinnedOnly)
    {
        return (!pinnedOnly || clip.IsPinned) &&
               (kind is null || clip.Kind == kind) &&
               MatchesFreeText(clip, query.FreeText) &&
               MatchesToken(clip.SourceUrl, clip.SourceDomain, query.Url) &&
               MatchesToken(clip.SourceApp, null, query.App) &&
               MatchesToken(clip.Tags, null, query.Tag);
    }

    private static bool MatchesFreeText(ClipboardClip clip, string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        return clip.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               clip.Preview.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               (clip.ReferencePath?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
               (clip.ContentText?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
               (clip.SourceApp?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
               (clip.SourceWindowTitle?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
               (clip.BrowserTabTitle?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
               (clip.SourceUrl?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
               (clip.SourceDomain?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
               (clip.Tags?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private static bool MatchesToken(string? primary, string? secondary, string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return true;
        }

        return (primary?.Contains(token, StringComparison.OrdinalIgnoreCase) ?? false) ||
               (secondary?.Contains(token, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private static SearchQuery ParseSearchQuery(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return default;
        }

        var url = string.Empty;
        var app = string.Empty;
        var tag = string.Empty;
        var freeTextTokens = new List<string>();

        foreach (var token in query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = token.IndexOf(':');
            if (separator > 0 && separator < token.Length - 1)
            {
                var key = token[..separator].Trim().ToLowerInvariant();
                var value = token[(separator + 1)..].Trim();
                if (value.Length > 0)
                {
                    try
                    {
                        value = Uri.UnescapeDataString(value);
                    }
                    catch
                    {
                    }
                }

                if (value.Length > 0)
                {
                    switch (key)
                    {
                        case "url":
                            url = value;
                            continue;
                        case "app":
                            app = value;
                            continue;
                        case "tag":
                            tag = value;
                            continue;
                    }
                }
            }

            freeTextTokens.Add(token);
        }

        var freeText = string.Join(' ', freeTextTokens).Trim();
        if (freeText.StartsWith('#'))
        {
            freeText = freeText.TrimStart('#').Trim();
        }

        return new SearchQuery(
            freeText.Length > 0 ? freeText : null,
            url.Length > 0 ? url : null,
            app.Length > 0 ? app : null,
            tag.Length > 0 ? tag : null);
    }

    private readonly record struct SearchQuery(string? FreeText, string? Url, string? App, string? Tag);

    public async Task<bool> ExistsAsync(string id, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var session = new Session(_instance);
            var dbid = OpenDatabase(session);
            using var table = new Table(session, dbid, TableName, OpenTableGrbit.ReadOnly);

            Api.JetSetCurrentIndex(session, table, "primary");
            Api.MakeKey(session, table, id, Encoding.Unicode, MakeKeyGrbit.NewKey);
            return Api.TrySeek(session, table, SeekGrbit.SeekEQ);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task AddAsync(ClipboardClip clip, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var session = new Session(_instance);
            var dbid = OpenDatabase(session);
            using var table = new Table(session, dbid, TableName, OpenTableGrbit.None);
            var columns = Columns(session, table);

            using var transaction = new Transaction(session);
            using var update = new Update(session, table, JET_prep.Insert);
            SetText(session, table, columns["Id"], clip.Id);
            SetText(session, table, columns["Kind"], clip.Kind.ToString());
            SetText(session, table, columns["Title"], clip.Title);
            SetText(session, table, columns["Preview"], clip.Preview);
            SetText(session, table, columns["ContentText"], clip.ContentText);
            SetBytes(session, table, columns["ContentBytes"], clip.ContentBytes);
            SetText(session, table, columns["ReferencePath"], clip.ReferencePath);
            SetText(session, table, columns["FormatsJson"], clip.FormatsJson);
            SetText(session, table, columns["SourceApp"], clip.SourceApp);
            SetText(session, table, columns["AppIconKey"], clip.AppIconKey);
            SetText(session, table, columns["SourceWindowTitle"], clip.SourceWindowTitle);
            SetText(session, table, columns["BrowserTabTitle"], clip.BrowserTabTitle);
            SetText(session, table, columns["SourceUrl"], clip.SourceUrl);
            SetText(session, table, columns["SourceDomain"], clip.SourceDomain);
            SetText(session, table, columns["Tags"], clip.Tags);
            SetText(session, table, columns["FormatLabel"], clip.FormatLabel);
            Api.SetColumn(session, table, columns["CopiedAt"], clip.CopiedAt.UtcTicks);
            Api.SetColumn(session, table, columns["IsPinned"], clip.IsPinned);
            Api.SetColumn(session, table, columns["UseCount"], clip.UseCount);
            update.Save();
            transaction.Commit(CommitTransactionGrbit.None);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SetPinnedAsync(string id, bool isPinned, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var session = new Session(_instance);
            var dbid = OpenDatabase(session);
            using var table = new Table(session, dbid, TableName, OpenTableGrbit.None);
            var columns = Columns(session, table);

            Api.JetSetCurrentIndex(session, table, "primary");
            Api.MakeKey(session, table, id, Encoding.Unicode, MakeKeyGrbit.NewKey);
            if (!Api.TrySeek(session, table, SeekGrbit.SeekEQ))
            {
                return;
            }

            using var transaction = new Transaction(session);
            using var update = new Update(session, table, JET_prep.Replace);
            Api.SetColumn(session, table, columns["IsPinned"], isPinned);
            update.Save();
            transaction.Commit(CommitTransactionGrbit.None);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var session = new Session(_instance);
            var dbid = OpenDatabase(session);
            using var table = new Table(session, dbid, TableName, OpenTableGrbit.None);

            Api.JetSetCurrentIndex(session, table, "primary");
            Api.MakeKey(session, table, id, Encoding.Unicode, MakeKeyGrbit.NewKey);
            if (!Api.TrySeek(session, table, SeekGrbit.SeekEQ))
            {
                return;
            }

            using var transaction = new Transaction(session);
            Api.JetDelete(session, table);
            transaction.Commit(CommitTransactionGrbit.None);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpdateTextAsync(string id, string title, string preview, string contentText, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var session = new Session(_instance);
            var dbid = OpenDatabase(session);
            using var table = new Table(session, dbid, TableName, OpenTableGrbit.None);
            var columns = Columns(session, table);

            Api.JetSetCurrentIndex(session, table, "primary");
            Api.MakeKey(session, table, id, Encoding.Unicode, MakeKeyGrbit.NewKey);
            if (!Api.TrySeek(session, table, SeekGrbit.SeekEQ))
            {
                return;
            }

            using var transaction = new Transaction(session);
            using var update = new Update(session, table, JET_prep.Replace);
            SetText(session, table, columns["Title"], title);
            SetText(session, table, columns["Preview"], preview);
            SetText(session, table, columns["ContentText"], contentText);
            update.Save();
            transaction.Commit(CommitTransactionGrbit.None);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpdateTagsAsync(string id, string? tags, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var session = new Session(_instance);
            var dbid = OpenDatabase(session);
            using var table = new Table(session, dbid, TableName, OpenTableGrbit.None);
            var columns = Columns(session, table);

            Api.JetSetCurrentIndex(session, table, "primary");
            Api.MakeKey(session, table, id, Encoding.Unicode, MakeKeyGrbit.NewKey);
            if (!Api.TrySeek(session, table, SeekGrbit.SeekEQ))
            {
                return;
            }

            using var transaction = new Transaction(session);
            using var update = new Update(session, table, JET_prep.Replace);
            SetText(session, table, columns["Tags"], tags ?? string.Empty);
            update.Save();
            transaction.Commit(CommitTransactionGrbit.None);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static IDictionary<string, JET_COLUMNID> Columns(Session session, JET_TABLEID table) =>
        Api.GetColumnDictionary(session, table);

    private static ClipboardClip ReadClip(Session session, JET_TABLEID table, IDictionary<string, JET_COLUMNID> columns)
    {
        var kindText = GetText(session, table, columns["Kind"]) ?? nameof(ClipKind.Other);
        var copiedAtTicks = Api.RetrieveColumnAsInt64(session, table, columns["CopiedAt"]) ?? DateTimeOffset.Now.UtcTicks;

        return new ClipboardClip
        {
            Id = GetText(session, table, columns["Id"]) ?? Guid.NewGuid().ToString("N"),
            Kind = Enum.TryParse<ClipKind>(kindText, out var kind) ? kind : ClipKind.Other,
            Title = GetText(session, table, columns["Title"]) ?? "Clipboard item",
            Preview = GetText(session, table, columns["Preview"]) ?? string.Empty,
            ContentText = GetText(session, table, columns["ContentText"]),
            ContentBytes = Api.RetrieveColumn(session, table, columns["ContentBytes"]),
            ReferencePath = GetText(session, table, columns["ReferencePath"]),
            FormatsJson = GetText(session, table, columns["FormatsJson"]),
            SourceApp = GetText(session, table, columns["SourceApp"]),
            AppIconKey = GetOptionalText(session, table, columns, "AppIconKey"),
            SourceWindowTitle = GetOptionalText(session, table, columns, "SourceWindowTitle"),
            BrowserTabTitle = GetOptionalText(session, table, columns, "BrowserTabTitle"),
            SourceUrl = GetOptionalText(session, table, columns, "SourceUrl"),
            SourceDomain = GetOptionalText(session, table, columns, "SourceDomain"),
            Tags = GetOptionalText(session, table, columns, "Tags"),
            FormatLabel = GetText(session, table, columns["FormatLabel"]),
            CopiedAt = new DateTimeOffset(copiedAtTicks, TimeSpan.Zero).ToLocalTime(),
            IsPinned = Api.RetrieveColumnAsBoolean(session, table, columns["IsPinned"]) ?? false,
            UseCount = Api.RetrieveColumnAsInt32(session, table, columns["UseCount"]) ?? 0
        };
    }

    private static void SetText(Session session, JET_TABLEID table, JET_COLUMNID column, string? value)
    {
        if (value is not null)
        {
            Api.SetColumn(session, table, column, value, Encoding.Unicode);
        }
    }

    private static string? GetText(Session session, JET_TABLEID table, JET_COLUMNID column) =>
        Api.RetrieveColumnAsString(session, table, column, Encoding.Unicode);

    private static string? GetOptionalText(Session session, JET_TABLEID table, IDictionary<string, JET_COLUMNID> columns, string columnName) =>
        columns.TryGetValue(columnName, out var column) ? GetText(session, table, column) : null;

    private static void SetBytes(Session session, JET_TABLEID table, JET_COLUMNID column, byte[]? value)
    {
        if (value is not null)
        {
            Api.SetColumn(session, table, column, value);
        }
    }

    private static int CountRows(Session session, JET_TABLEID table)
    {
        var count = 0;
        if (!Api.TryMoveFirst(session, table))
        {
            return count;
        }

        do
        {
            count++;
        }
        while (Api.TryMoveNext(session, table));

        return count;
    }

}
