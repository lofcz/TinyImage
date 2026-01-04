using System;

namespace TinyImage.Codecs.Gif;

/// <summary>
/// NeuQuant Neural-Net Quantization Algorithm.
/// Copyright (c) 1994 Anthony Dekker
/// 
/// NEUQUANT Neural-Net quantization algorithm by Anthony Dekker, 1994.
/// See "Kohonen neural networks for optimal colour quantization"
/// in "Network: Computation in Neural Systems" Vol. 5 (1994) pp 351-367.
/// 
/// Enhanced implementation with alpha channel support, floating-point learning,
/// and squared distance color matching based on the Rust/pngnq implementation.
/// </summary>
internal sealed class NeuQuant
{
    private const int RadiusDec = 30;           // factor of 1/30 each cycle
    private const int AlphaBiasShift = 10;      // alpha starts at 1.0
    private const int InitAlpha = 1 << AlphaBiasShift;

    private const double Gamma = 1024.0;
    private const double Beta = 1.0 / Gamma;
    private const double BetaGamma = Beta * Gamma;

    // four primes near 500 - assume no image has a length so large
    // that it is divisible by all four primes
    private static readonly int[] Primes = { 499, 491, 487, 503 };

    private readonly double[][] _network;       // the network itself - [netsize][4] RGBA
    private readonly byte[][] _colormap;        // the final color map [netsize][4] RGBA
    private readonly int[] _netIndex;           // for network lookup (indexed by green)
    private readonly double[] _bias;            // bias array for learning
    private readonly double[] _freq;            // freq array for learning
    private readonly int _netSize;              // number of colors (typically 256)
    private readonly int _sampleFac;            // sampling factor 1..30

    /// <summary>
    /// Creates a new NeuQuant quantizer.
    /// </summary>
    /// <param name="pixels">RGBA pixel data (R, G, B, A, R, G, B, A, ...) or RGB data (R, G, B, R, G, B, ...).</param>
    /// <param name="sampleFac">Sampling factor (1-30). Lower = better quality but slower.</param>
    /// <param name="colors">Number of colors in the palette (default 256).</param>
    /// <param name="hasAlpha">Whether the pixel data includes alpha channel (4 bytes per pixel vs 3).</param>
    public NeuQuant(byte[] pixels, int sampleFac = 10, int colors = 256, bool hasAlpha = false)
    {
        if (pixels == null)
            throw new ArgumentNullException(nameof(pixels));

        _netSize = Math.Max(4, Math.Min(256, colors));
        _sampleFac = Math.Max(1, Math.Min(30, sampleFac));

        _network = new double[_netSize][];
        _colormap = new byte[_netSize][];
        _netIndex = new int[256];
        _bias = new double[_netSize];
        _freq = new double[_netSize];

        // Initialize network
        double freqInit = 1.0 / _netSize;
        for (int i = 0; i < _netSize; i++)
        {
            double val = (i * 256.0) / _netSize;
            // Alpha initialization: set alpha values lower for dark pixels to avoid
            // fully transparent black pixels in the palette
            double alpha = i < 16 ? i * 16.0 : 255.0;

            _network[i] = new double[] { val, val, val, alpha };
            _colormap[i] = new byte[] { 0, 0, 0, 255 };
            _freq[i] = freqInit;
            _bias[i] = 0.0;
        }

        // Learn from pixels
        Learn(pixels, hasAlpha);

        // Build color map and index
        BuildColorMap();
        BuildNetIndex();
    }

    /// <summary>
    /// Processes the image and returns the color palette.
    /// </summary>
    /// <returns>The color palette as RGB bytes (netSize * 3 bytes total).</returns>
    public byte[] Process()
    {
        // Already processed in constructor, just return the RGB color map
        var map = new byte[_netSize * 3];
        for (int i = 0; i < _netSize; i++)
        {
            map[i * 3] = _colormap[i][0];     // R
            map[i * 3 + 1] = _colormap[i][1]; // G
            map[i * 3 + 2] = _colormap[i][2]; // B
        }
        return map;
    }

    /// <summary>
    /// Maps a color to the nearest palette index using squared distance.
    /// </summary>
    public int Map(int r, int g, int b)
    {
        return SearchNetIndex((byte)b, (byte)g, (byte)r, 255);
    }

    /// <summary>
    /// Maps a color with alpha to the nearest palette index using squared distance.
    /// </summary>
    public int Map(int r, int g, int b, int a)
    {
        return SearchNetIndex((byte)b, (byte)g, (byte)r, (byte)a);
    }

    /// <summary>
    /// Move neuron i towards biased (r,g,b,a) by factor alpha.
    /// </summary>
    private void AlterSingle(double alpha, int i, double r, double g, double b, double a)
    {
        var n = _network[i];
        n[0] -= alpha * (n[0] - r);
        n[1] -= alpha * (n[1] - g);
        n[2] -= alpha * (n[2] - b);
        n[3] -= alpha * (n[3] - a);
    }

    /// <summary>
    /// Move adjacent neurons towards biased (r,g,b,a) by factor alpha.
    /// </summary>
    private void AlterNeighbour(double alpha, int rad, int i, double r, double g, double b, double a)
    {
        int lo = Math.Max(i - rad, 0);
        int hi = Math.Min(i + rad, _netSize);

        int j = i + 1;
        int k = i - 1;
        int q = 0;

        double radSq = rad * rad;

        while (j < hi || k > lo)
        {
            double neighborAlpha = alpha * (radSq - q * q) / radSq;
            q++;

            if (j < hi)
            {
                var p = _network[j];
                p[0] -= neighborAlpha * (p[0] - r);
                p[1] -= neighborAlpha * (p[1] - g);
                p[2] -= neighborAlpha * (p[2] - b);
                p[3] -= neighborAlpha * (p[3] - a);
                j++;
            }

            if (k > lo)
            {
                var p = _network[k];
                p[0] -= neighborAlpha * (p[0] - r);
                p[1] -= neighborAlpha * (p[1] - g);
                p[2] -= neighborAlpha * (p[2] - b);
                p[3] -= neighborAlpha * (p[3] - a);
                k--;
            }
        }
    }

    /// <summary>
    /// Search for biased RGBA values.
    /// Finds closest neuron (min dist) and updates freq.
    /// Finds best neuron (min dist-bias) and returns position.
    /// For frequently chosen neurons, freq[i] is high and bias[i] is negative.
    /// </summary>
    private int Contest(double r, double g, double b, double a)
    {
        double bestd = double.MaxValue;
        double bestbiasd = double.MaxValue;
        int bestpos = 0;
        int bestbiaspos = 0;

        for (int i = 0; i < _netSize; i++)
        {
            var n = _network[i];

            // Early termination optimization
            double bestbiasdBiased = bestbiasd + _bias[i];

            // Start with blue difference (arbitrary choice, but consistent with sorting by green)
            double dist = Math.Abs(n[2] - b);
            dist += Math.Abs(n[0] - r);

            if (dist < bestd || dist < bestbiasdBiased)
            {
                dist += Math.Abs(n[1] - g);
                dist += Math.Abs(n[3] - a);

                if (dist < bestd)
                {
                    bestd = dist;
                    bestpos = i;
                }

                double biasdist = dist - _bias[i];
                if (biasdist < bestbiasd)
                {
                    bestbiasd = biasdist;
                    bestbiaspos = i;
                }
            }

            _freq[i] -= Beta * _freq[i];
            _bias[i] += BetaGamma * _freq[i];
        }

        _freq[bestpos] += Beta;
        _bias[bestpos] -= BetaGamma;

        return bestbiaspos;
    }

    /// <summary>
    /// Main learning loop.
    /// </summary>
    private void Learn(byte[] pixels, bool hasAlpha)
    {
        int bytesPerPixel = hasAlpha ? 4 : 3;
        int pixelCount = pixels.Length / bytesPerPixel;

        if (pixelCount == 0)
            return;

        int initRad = _netSize >> 3;    // for 256 cols, radius starts at 32
        int radiusBiasShift = 6;
        int radiusBias = 1 << radiusBiasShift;
        int initBiasRadius = initRad * radiusBias;
        int biasRadius = initBiasRadius;

        int alphaDec = 30 + ((_sampleFac - 1) / 3);
        int samplePixels = pixelCount / _sampleFac;

        // Dynamic learning cycles based on network size
        int nCycles = Math.Max(100, _netSize >> 1);
        int delta = Math.Max(1, samplePixels / nCycles);

        int alpha = InitAlpha;
        int rad = biasRadius >> radiusBiasShift;
        if (rad <= 1) rad = 0;

        // Determine step size - must be coprime with pixel count
        int step = Primes[3]; // default
        foreach (int prime in Primes)
        {
            if (pixelCount % prime != 0)
            {
                step = prime;
                break;
            }
        }

        int pos = 0;
        int count = 0;

        for (int i = 0; i < samplePixels; i++)
        {
            int pixelOffset = pos * bytesPerPixel;

            // Read RGBA values
            double r = pixels[pixelOffset];
            double g = pixels[pixelOffset + 1];
            double b = pixels[pixelOffset + 2];
            double a = hasAlpha ? pixels[pixelOffset + 3] : 255.0;

            int j = Contest(r, g, b, a);

            double alphaFactor = (double)alpha / InitAlpha;
            AlterSingle(alphaFactor, j, r, g, b, a);

            if (rad > 0)
            {
                AlterNeighbour(alphaFactor, rad, j, r, g, b, a);
            }

            pos += step;
            if (pos >= pixelCount)
                pos -= pixelCount;

            count++;
            if (count % delta == 0)
            {
                alpha -= alpha / alphaDec;
                biasRadius -= biasRadius / RadiusDec;
                rad = biasRadius >> radiusBiasShift;
                if (rad <= 1) rad = 0;
            }
        }
    }

    /// <summary>
    /// Build the color map from the network.
    /// </summary>
    private void BuildColorMap()
    {
        for (int i = 0; i < _netSize; i++)
        {
            var n = _network[i];
            _colormap[i][0] = (byte)Math.Round(Math.Max(0, Math.Min(255, n[0]))); // R
            _colormap[i][1] = (byte)Math.Round(Math.Max(0, Math.Min(255, n[1]))); // G
            _colormap[i][2] = (byte)Math.Round(Math.Max(0, Math.Min(255, n[2]))); // B
            _colormap[i][3] = (byte)Math.Round(Math.Max(0, Math.Min(255, n[3]))); // A
        }
    }

    /// <summary>
    /// Insertion sort of network and building of netindex[0..255].
    /// Index is built on green channel for efficient lookup.
    /// </summary>
    private void BuildNetIndex()
    {
        int previouscol = 0;
        int startpos = 0;

        for (int i = 0; i < _netSize; i++)
        {
            var p = _colormap[i];
            int smallpos = i;
            int smallval = p[1]; // index on green

            // find smallest in i..netsize-1
            for (int j = i + 1; j < _netSize; j++)
            {
                var q = _colormap[j];
                if (q[1] < smallval)
                {
                    smallpos = j;
                    smallval = q[1];
                }
            }

            // swap entries
            if (i != smallpos)
            {
                var temp = _colormap[smallpos];
                _colormap[smallpos] = _colormap[i];
                _colormap[i] = temp;
            }

            // smallval entry is now in position i
            if (smallval != previouscol)
            {
                _netIndex[previouscol] = (startpos + i) >> 1;
                for (int j = previouscol + 1; j < smallval; j++)
                {
                    _netIndex[j] = i;
                }
                previouscol = smallval;
                startpos = i;
            }
        }

        int maxNetPos = _netSize - 1;
        _netIndex[previouscol] = (startpos + maxNetPos) >> 1;
        for (int j = previouscol + 1; j < 256; j++)
        {
            _netIndex[j] = maxNetPos;
        }
    }

    /// <summary>
    /// Search for best matching color using squared distance.
    /// </summary>
    private int SearchNetIndex(byte b, byte g, byte r, byte a)
    {
        int bestDist = int.MaxValue;
        int firstGuess = _netIndex[g];
        int bestPos = firstGuess;

        // Search forward from first guess
        for (int i = firstGuess; i < _netSize; i++)
        {
            var c = _colormap[i];

            // Squared distance on green first (since we index by green)
            int dg = c[1] - g;
            int dist = dg * dg;

            if (dist > bestDist)
                break; // Green is sorted, so we can stop

            int dr = c[0] - r;
            dist += dr * dr;
            if (dist >= bestDist)
                continue;

            int db = c[2] - b;
            dist += db * db;
            if (dist >= bestDist)
                continue;

            int da = c[3] - a;
            dist += da * da;
            if (dist >= bestDist)
                continue;

            bestDist = dist;
            bestPos = i;
        }

        // Search backward from first guess
        for (int i = firstGuess - 1; i >= 0; i--)
        {
            var c = _colormap[i];

            // Squared distance on green first
            int dg = g - c[1];
            int dist = dg * dg;

            if (dist > bestDist)
                break; // Green is sorted, so we can stop

            int dr = c[0] - r;
            dist += dr * dr;
            if (dist >= bestDist)
                continue;

            int db = c[2] - b;
            dist += db * db;
            if (dist >= bestDist)
                continue;

            int da = c[3] - a;
            dist += da * da;
            if (dist >= bestDist)
                continue;

            bestDist = dist;
            bestPos = i;
        }

        return bestPos;
    }
}
