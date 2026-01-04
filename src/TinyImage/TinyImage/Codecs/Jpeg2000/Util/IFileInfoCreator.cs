namespace TinyImage.Codecs.Jpeg2000.Util
{
    internal interface IFileInfoCreator
    {
        #region METHODS

        IFileInfo Create(string fileName);

        #endregion
    }
}