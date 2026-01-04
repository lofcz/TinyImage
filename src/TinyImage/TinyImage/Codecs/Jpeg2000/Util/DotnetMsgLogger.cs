// Copyright (c) 2007-2016 CSJ2K contributors.
// Licensed under the BSD 3-Clause License.

namespace TinyImage.Codecs.Jpeg2000.Util
{
    using j2k.util;
    using System;

    internal class DotnetMsgLogger : StreamMsgLogger
    {
        #region FIELDS

        private static readonly IMsgLogger Instance = new DotnetMsgLogger();

        #endregion

        #region CONSTRUCTORS

        public DotnetMsgLogger()
            : base(Console.OpenStandardOutput(), Console.OpenStandardError(), 78)
        {
        }

        #endregion

        #region METHODS

        public static void Register()
        {
            FacilityManager.DefaultMsgLogger = Instance;
        }

        #endregion
    }
}
