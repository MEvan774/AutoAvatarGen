using System;
using System.IO;
using System.Text;

namespace MugsTech.Tts
{
    /// <summary>
    /// Minimal WAV read/write plus raw-PCM wrapping.
    ///
    /// The chunked pipeline works on plain interleaved float samples: the API's
    /// <c>pcm_44100</c> output arrives headerless and gets a WAV header bolted
    /// on here, ffmpeg hands audio back as WAV, and the splitter's per-section
    /// output goes out as WAV before its final encode. Unity's own audio
    /// importer can't do any of that from a byte array, hence this.
    ///
    /// Pure C#, no Unity dependencies.
    /// </summary>
    public static class WavCodec
    {
        public class AudioBuffer
        {
            public float[] Samples;      // interleaved
            public int     Channels;
            public int     SampleRate;

            public int   Frames  => Channels > 0 ? Samples.Length / Channels : 0;
            public float Seconds => SampleRate > 0 ? Frames / (float)SampleRate : 0f;
        }

        // ---- raw PCM -----------------------------------------------------

        /// <summary>
        /// Interpret headerless 16-bit little-endian PCM (what ElevenLabs'
        /// <c>pcm_*</c> output formats return) as float samples.
        /// </summary>
        public static AudioBuffer FromPcm16(byte[] pcm, int sampleRate, int channels)
        {
            if (pcm == null) pcm = Array.Empty<byte>();
            int count = pcm.Length / 2;
            var samples = new float[count];
            for (int i = 0; i < count; i++)
            {
                short s = (short)(pcm[i * 2] | (pcm[i * 2 + 1] << 8));
                samples[i] = s / 32768f;
            }
            return new AudioBuffer {
                Samples = samples, Channels = Math.Max(1, channels), SampleRate = sampleRate
            };
        }

        // ---- write -------------------------------------------------------

        public static void Write(string path, AudioBuffer buffer)
            => Write(path, buffer.Samples, buffer.SampleRate, buffer.Channels);

        /// <summary>16-bit PCM WAV — the format ffmpeg and Unity both read without argument.</summary>
        public static void Write(string path, float[] samples, int sampleRate, int channels)
        {
            samples  = samples ?? Array.Empty<float>();
            channels = Math.Max(1, channels);

            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            int dataBytes = samples.Length * 2;
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (var w  = new BinaryWriter(fs))
            {
                w.Write(Encoding.ASCII.GetBytes("RIFF"));
                w.Write(36 + dataBytes);
                w.Write(Encoding.ASCII.GetBytes("WAVE"));

                w.Write(Encoding.ASCII.GetBytes("fmt "));
                w.Write(16);                                  // PCM header size
                w.Write((short)1);                            // format = PCM
                w.Write((short)channels);
                w.Write(sampleRate);
                w.Write(sampleRate * channels * 2);           // byte rate
                w.Write((short)(channels * 2));               // block align
                w.Write((short)16);                           // bits per sample

                w.Write(Encoding.ASCII.GetBytes("data"));
                w.Write(dataBytes);
                foreach (float f in samples)
                {
                    float c = f < -1f ? -1f : (f > 1f ? 1f : f);
                    w.Write((short)Math.Round(c * 32767f));
                }
            }
        }

        // ---- read --------------------------------------------------------

        public static AudioBuffer Read(string path)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read))
                return Read(fs);
        }

        public static AudioBuffer Read(byte[] bytes)
        {
            using (var ms = new MemoryStream(bytes ?? Array.Empty<byte>()))
                return Read(ms);
        }

        /// <summary>
        /// Parse a RIFF/WAVE stream. Handles 16/24/32-bit integer and 32-bit
        /// float samples and skips chunks it doesn't recognise (ffmpeg likes to
        /// emit a LIST/INFO chunk ahead of the data).
        /// </summary>
        public static AudioBuffer Read(Stream stream)
        {
            using (var r = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true))
            {
                if (new string(r.ReadChars(4)) != "RIFF") throw new Exception("Not a RIFF file.");
                r.ReadInt32();                                    // riff size
                if (new string(r.ReadChars(4)) != "WAVE") throw new Exception("Not a WAVE file.");

                int   channels = 1, sampleRate = 44100, bits = 16, format = 1;
                bool  haveFmt  = false;

                while (r.BaseStream.Position + 8 <= r.BaseStream.Length)
                {
                    string id   = new string(r.ReadChars(4));
                    int    size = r.ReadInt32();
                    if (size < 0) throw new Exception($"Bad chunk size in '{id}'.");
                    long   next = r.BaseStream.Position + size + (size & 1);   // chunks pad to even

                    if (id == "fmt ")
                    {
                        format     = r.ReadInt16();
                        channels   = Math.Max(1, (int)r.ReadInt16());
                        sampleRate = r.ReadInt32();
                        r.ReadInt32();                            // byte rate
                        r.ReadInt16();                            // block align
                        bits       = r.ReadInt16();

                        // WAVE_FORMAT_EXTENSIBLE hides the real format in the
                        // extension's GUID; bit depth is enough to tell them apart.
                        if (format == unchecked((short)0xFFFE)) format = bits == 32 ? 3 : 1;
                        haveFmt = true;
                    }
                    else if (id == "data")
                    {
                        if (!haveFmt) throw new Exception("WAV data chunk before fmt chunk.");
                        byte[] data = r.ReadBytes(size);
                        return new AudioBuffer {
                            Samples    = Decode(data, format, bits),
                            Channels   = channels,
                            SampleRate = sampleRate,
                        };
                    }

                    r.BaseStream.Seek(next, SeekOrigin.Begin);
                }
                throw new Exception("WAV file has no data chunk.");
            }
        }

        static float[] Decode(byte[] data, int format, int bits)
        {
            if (format == 3 && bits == 32)
            {
                var outF = new float[data.Length / 4];
                Buffer.BlockCopy(data, 0, outF, 0, outF.Length * 4);
                return outF;
            }

            switch (bits)
            {
                case 16:
                {
                    var o = new float[data.Length / 2];
                    for (int i = 0; i < o.Length; i++)
                        o[i] = (short)(data[i * 2] | (data[i * 2 + 1] << 8)) / 32768f;
                    return o;
                }
                case 24:
                {
                    var o = new float[data.Length / 3];
                    for (int i = 0; i < o.Length; i++)
                    {
                        int v = data[i * 3] | (data[i * 3 + 1] << 8) | (data[i * 3 + 2] << 16);
                        if ((v & 0x800000) != 0) v |= unchecked((int)0xFF000000);   // sign-extend
                        o[i] = v / 8388608f;
                    }
                    return o;
                }
                case 32:
                {
                    var o = new float[data.Length / 4];
                    for (int i = 0; i < o.Length; i++)
                        o[i] = BitConverter.ToInt32(data, i * 4) / 2147483648f;
                    return o;
                }
                case 8:
                {
                    var o = new float[data.Length];
                    for (int i = 0; i < o.Length; i++) o[i] = (data[i] - 128) / 128f;
                    return o;
                }
                default:
                    throw new Exception($"Unsupported WAV sample depth: {bits}-bit (format {format}).");
            }
        }
    }
}
