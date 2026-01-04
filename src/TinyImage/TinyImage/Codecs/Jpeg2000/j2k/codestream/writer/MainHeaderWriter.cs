// Copyright (c) 2025 Sjofn LLC.
// Licensed under the BSD 3-Clause License.

using TinyImage.Codecs.Jpeg2000.j2k.encoder;
using TinyImage.Codecs.Jpeg2000.j2k.wavelet.analysis;
using TinyImage.Codecs.Jpeg2000.j2k.entropy;

namespace TinyImage.Codecs.Jpeg2000.j2k.codestream.writer
{
    /// <summary>
    /// Manages writing of main header sections.
    /// </summary>
    internal class MainHeaderWriter
    {
        private readonly EncoderSpecs encSpec;
        private readonly ForwardWT dwt;
        private readonly int nComp;

        public MainHeaderWriter(EncoderSpecs encSpec, ForwardWT dwt, int nComp)
        {
            this.encSpec = encSpec;
            this.dwt = dwt;
            this.nComp = nComp;
        }

        public bool ShouldWriteCOC(int compIdx, bool isEresUsed)
        {
            var isEresUsedinComp = ((string)encSpec.tts.getCompDef(compIdx)).Equals("predict");
            
            return encSpec.wfs.isCompSpecified(compIdx) ||
                   encSpec.dls.isCompSpecified(compIdx) ||
                   encSpec.bms.isCompSpecified(compIdx) ||
                   encSpec.mqrs.isCompSpecified(compIdx) ||
                   encSpec.rts.isCompSpecified(compIdx) ||
                   encSpec.sss.isCompSpecified(compIdx) ||
                   encSpec.css.isCompSpecified(compIdx) ||
                   encSpec.pss.isCompSpecified(compIdx) ||
                   encSpec.cblks.isCompSpecified(compIdx) ||
                   (isEresUsed != isEresUsedinComp);
        }

        public bool ShouldWriteQCC(int compIdx, int defimgn)
        {
            return dwt.getNomRangeBits(compIdx) != defimgn ||
                   encSpec.qts.isCompSpecified(compIdx) ||
                   encSpec.qsss.isCompSpecified(compIdx) ||
                   encSpec.dls.isCompSpecified(compIdx) ||
                   encSpec.gbs.isCompSpecified(compIdx);
        }

        public bool ShouldWritePOC()
        {
            var prog = (Progression[])(encSpec.pocs.getDefault());
            return prog.Length > 1;
        }
    }
}
