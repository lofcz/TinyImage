namespace TinyImage.Codecs.Jpeg2000.Util
{
    internal interface IFileInfo
    {
        #region PROPERTIES

        string Name { get; }

        string FullName { get; }

        bool Exists { get; }

        #endregion

        #region METHODS

        bool Delete();

        #endregion
    }
}