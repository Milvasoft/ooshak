using System.Windows.Markup;
using Ooshak.Core;

namespace Ooshak;

/// <summary>XAML access to the translations: <c>Text="{local:T 'Ayarlar'}"</c>.</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TExtension(string text) : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider) => L.T(text);
}
