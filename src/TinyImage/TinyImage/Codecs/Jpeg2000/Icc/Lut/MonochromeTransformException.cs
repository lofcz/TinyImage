/// <summary>**************************************************************************
/// 
/// $Id: MonochromeTransformException.java,v 1.1 2002/07/25 14:56:49 grosbois Exp $
/// 
/// Copyright Eastman Kodak Company, 343 State Street, Rochester, NY 14650
/// $Date $
/// ***************************************************************************
/// </summary>
using System;
namespace TinyImage.Codecs.Jpeg2000.Icc.Lut
{

    /// <summary> Exception thrown by MonochromeTransformTosRGB.
    /// 
    /// </summary>
    /// <seealso cref="j2k.icc.lut.MonochromeTransformTosRGB" />
    /// <version> 	1.0
    /// </version>
    /// <author> 	Bruce A. Kern
    /// </author>

    internal class MonochromeTransformException : Exception
    {

        /// <summary> Contruct with message</summary>
        /// <param name="msg">returned by getMessage()
        /// </param>
        internal MonochromeTransformException(string msg) : base(msg)
        {
        }

        /// <summary> Empty constructor</summary>
        internal MonochromeTransformException()
        {
        }

        /* end class MonochromeTransformException */
    }
}