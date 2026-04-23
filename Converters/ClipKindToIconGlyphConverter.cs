using Clipman.Models;
using Microsoft.UI.Xaml.Data;

namespace Clipman.Converters;

public sealed class ClipKindToIconGlyphConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value switch
        {
            ClipKind.Code => "\uE943",
            ClipKind.Url => "\uE71B",
            ClipKind.Image => "\uEB9F",
            ClipKind.Video => "\uE714",
            ClipKind.Html => "\uE8A5",
            ClipKind.File => "\uE8A5",
            ClipKind.Other => "\uE8FD",
            _ => "\uE8C8"
        };

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
