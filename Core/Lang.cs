using System.Globalization;
using System.Windows.Markup;

namespace Atomic.Core;

/// <summary>UI text lookup. The English text is the key; anything without a Thai entry stays English.</summary>
public static partial class L
{
    public static bool Thai { get; private set; }

    /// <summary>"en" or "th"; anything else follows the Windows display language / region.</summary>
    public static void Set(string? lang)
    {
        if (lang is not ("en" or "th"))
            lang = CultureInfo.CurrentUICulture.Name.StartsWith("th") || CultureInfo.CurrentCulture.Name.StartsWith("th") ? "th" : "en";
        Thai = lang == "th";
    }

    public static string T(string en) => Thai && Th.TryGetValue(en, out var th) ? th : en;
    public static string F(string en, params object?[] args) => string.Format(T(en), args);
}

/// <summary>XAML: Text="{l:T 'Some text'}"</summary>
[MarkupExtensionReturnType(typeof(string))]
public class TExtension(string key) : MarkupExtension
{
    public string Key { get; set; } = key;
    public override object ProvideValue(IServiceProvider serviceProvider) => L.T(Key);
}

/// <summary>XAML: Text="{l:U 'Some text'}" - the same text in capitals, for labels, titles and the main buttons (Thai has no capitals and stays as it is).</summary>
[MarkupExtensionReturnType(typeof(string))]
public class UExtension(string key) : MarkupExtension
{
    public string Key { get; set; } = key;
    public override object ProvideValue(IServiceProvider serviceProvider) => L.T(Key).ToUpperInvariant();
}
