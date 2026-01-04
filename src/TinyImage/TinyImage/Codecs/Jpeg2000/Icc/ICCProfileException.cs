/// <summary>**************************************************************************
/// 
/// $Id: ICCProfileException.java,v 1.2 2002/08/08 14:08:13 grosbois Exp $
/// 
/// Copyright Eastman Kodak Company, 343 State Street, Rochester, NY 14650
/// $Date $
/// ***************************************************************************
/// </summary>
using System;
namespace TinyImage.Codecs.Jpeg2000.Icc
{

    /// <summary> This exception is thrown when the content of a profile
    /// is incorrect.
    /// 
    /// </summary>
    /// <seealso cref="j2k.icc.ICCProfile" />
    /// <version> 	1.0
    /// </version>
    /// <author> 	Bruce A. Kern
    /// </author>
    internal class ICCProfileException : Exception
    {

        /// <summary>  Contruct with message</summary>
        /// <param name="msg">returned by getMessage()
        /// </param>
        public ICCProfileException(string msg) : base(msg)
        {
        }


        /// <summary> Empty constructor</summary>
        public ICCProfileException()
        {
        }
    }
}