namespace Backend.Models;

/// <summary>
///   Information about a URL that a plugin can return.
/// </summary>
[Flags]
public enum UrlInformation
{
    /// <summary>
    ///   Unsupported / unknown URL
    /// </summary>
    Unknown = 0,

    /// <summary>
    ///   If the URL just points to a content file (e.g. a PNG, JPEG, another image, etc.)
    /// </summary>
    ContentLink = 1,

    /// <summary>
    ///    Link is to a page that contains a single media. For example, a page that has a big image on it.
    /// </summary>
    ContentPage = 2,

    /// <summary>
    ///    Link is to a page that contains a gallery of media. For example, a page that has a list of images on it.
    /// </summary>
    Gallery = 4,

    /// <summary>
    ///   Link to a page in a gallery that has navigation to other items but also may be a content page.
    /// </summary>
    GalleryPage = 8,

    /// <summary>
    ///   Explicit page with known big content and links to other items.
    /// </summary>
    GalleryPageContent = GalleryPage | ContentPage,
}
