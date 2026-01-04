# Third-Party Notices

TinyImage includes code from the following open source projects.

---

## BigGustave

**License:** The Unlicense (Public Domain)
**Source:** https://github.com/EliotJones/BigGustave

BigGustave is used for PNG encoding and decoding.

```
This is free and unencumbered software released into the public domain.

Anyone is free to copy, modify, publish, use, compile, sell, or
distribute this software, either in source code form or as a compiled
binary, for any purpose, commercial or non-commercial, and by any
means.

In jurisdictions that recognize copyright laws, the author or authors
of this software dedicate any and all copyright interest in the
software to the public domain. We make this dedication for the benefit
of the public at large and to the detriment of our heirs and
successors. We intend this dedication to be an overt act of
relinquishment in perpetuity of all present and future rights to this
software under copyright law.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.
IN NO EVENT SHALL THE AUTHORS BE LIABLE FOR ANY CLAIM, DAMAGES OR
OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE,
ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
OTHER DEALINGS IN THE SOFTWARE.

For more information, please refer to <http://unlicense.org/>
```

---

## JpegLibrary

**License:** MIT License
**Source:** https://github.com/yigolden/JpegLibrary

JpegLibrary is used for JPEG encoding and decoding.

```
MIT License

Copyright (c) yigolden

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

---

## UniGif

**License:** MIT License
**Source:** https://github.com/WestHillApps/UniGif

UniGif is used for GIF decoding (LZW decompression, GIF format parsing).

```
MIT License

Copyright (c) WestHillApps

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

---

## AnimatedGifEncoder / NeuQuant

**License:** MIT License / Public Domain
**Source:** https://github.com/mrousavy/AnimatedGif

AnimatedGifEncoder is used for GIF encoding (LZW compression, color quantization).
NeuQuant algorithm by Anthony Dekker (1994) for neural network color quantization.

```
MIT License

Copyright (c) mrousavy

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

JPEG 2000 Codec (CoreJ2K)
=========================
https://github.com/cinderblocks/CoreJ2K/commits/master/ - f6fe2c2ffff4ac5a82ea4ed8ca4267a0660856ca

The JPEG 2000 codec in TinyImage is based on CoreJ2K, which is derived from CSJ2K
(C# port of the JJ2000 reference implementation).

--------------------------------------------------------------------------------
CoreJ2K/CSJ2K License (BSD 3-Clause)
--------------------------------------------------------------------------------

Copyright (c) 2007-2016, CSJ2K contributors.
Copyright (c) 2024-2025, Sjofn LLC.

Redistribution and use in source and binary forms, with or without modification, 
are permitted provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice, 
   this list of conditions and the following disclaimer.

2. Redistributions in binary form must reproduce the above copyright notice, 
   this list of conditions and the following disclaimer in the documentation 
   and/or other materials provided with the distribution.

3. Neither the name of the copyright holder nor the names of its contributors 
   may be used to endorse or promote products derived from this software without 
   specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND 
ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED 
WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. 
IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, 
INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT
NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, 
OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, 
WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) 
ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE 
POSSIBILITY OF SUCH DAMAGE.

--------------------------------------------------------------------------------
JJ2000 License
--------------------------------------------------------------------------------

This software module was originally developed by Raphael Grosbois and
Diego Santa Cruz (Swiss Federal Institute of Technology-EPFL); Joel
Askelof (Ericsson Radio Systems AB); and Bertrand Berthelot, David
Bouchard, Felix Henry, Gerard Mozelle and Patrice Onno (Canon Research
Centre France S.A) in the course of development of the JPEG2000
standard as specified by ISO/IEC 15444 (JPEG 2000 Standard). This
software module is an implementation of a part of the JPEG 2000
Standard. Swiss Federal Institute of Technology-EPFL, Ericsson Radio
Systems AB and Canon Research Centre France S.A (collectively JJ2000
Partners) agree not to assert against ISO/IEC and users of the JPEG
2000 Standard (Users) any of their rights under the copyright, not
including other intellectual property rights, for this software module
with respect to the usage by ISO/IEC and Users of this software module
or modifications thereof for use in hardware or software products
claiming conformance to the JPEG 2000 Standard. Those intending to use
this software module in hardware or software products are advised that
their use may infringe existing patents. The original developers of
this software module, JJ2000 Partners and ISO/IEC assume no liability
for use of this software module or modifications thereof. No license
or right to this software module is granted for non JPEG 2000 Standard
conforming products. JJ2000 Partners have full right to use this
software module for his/her own purpose, assign or donate this
software module to any third party and to inhibit third parties from
using this software module for non JPEG 2000 Standard conforming
products. This copyright notice must be included in all copies or
derivative works of this software module.

Copyright (c) 1999/2000 JJ2000 Partners.
