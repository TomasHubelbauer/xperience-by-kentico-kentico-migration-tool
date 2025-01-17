using CMS.ContentEngine.Internal;

namespace Migration.Tool.Core.K11.Providers;

internal class ContentItemNameValidator : IContentItemNameValidator
{
    /// <inheritdoc />
    public bool IsUnique(string name) => IsUnique(0, name);


    /// <inheritdoc />
    public bool IsUnique(int id, string name)
    {
        var contentItemInfo = new ContentItemInfo { ContentItemID = id, ContentItemName = name };

        return contentItemInfo.CheckUniqueCodeName();
    }
}
