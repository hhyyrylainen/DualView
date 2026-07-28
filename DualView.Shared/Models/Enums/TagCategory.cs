namespace DualView.Shared.Models.Enums;

public enum TagCategory
{
    /// <summary>
    ///   Describes an object or a character in the image
    /// </summary>
    DescribeCharacterObject = 0,

    /// <summary>
    ///   Tags a character or a person in the image
    /// </summary>
    CharacterPerson = 1,

    /// <summary>
    ///   Tags something that's not immediately visible from the image or relates to something meta, like captions or artist name
    /// </summary>
    Meta = 2,

    /// <summary>
    ///   Tags the series or universe this image belongs to, for example Star Wars. Or another loosely defined series
    /// </summary>
    SeriesUniverse = 3,

    /// <summary>
    ///   Tags an action that is being performed
    /// </summary>
    Action = 4,

    /// <summary>
    ///   Tags the image's level of "helpfulness / quality". Only one tag of this type should apply to an image.
    /// </summary>
    Quality = 5,
}
