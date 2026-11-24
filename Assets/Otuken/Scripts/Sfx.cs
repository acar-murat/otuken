using System.Collections.Generic;
using UnityEngine;

namespace Otuken
{
    /// <summary>Tiny synthesized sound effects, generated once at start so the project ships no audio files.</summary>
    public sealed class Sfx
    {
        enum Wave { Sine, Triangle, Square, Saw }

        readonly AudioSource src;
        readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        public bool Muted;

        public Sfx(GameObject host)
        {
            src = host.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.spatialBlend = 0f;
            Make("bow", 520, 240, 0.07f, Wave.Triangle, 0.35f);
            Make("orb", 700, 900, 0.08f, Wave.Sine, 0.3f);
            Make("swing", 180, 90, 0.08f, Wave.Saw, 0.25f);
            Make("hit", 140, 60, 0.1f, Wave.Square, 0.2f);
            Make("hurt", 90, 50, 0.15f, Wave.Saw, 0.35f);
            Make("skill", 300, 600, 0.2f, Wave.Triangle, 0.4f);
            Make("ult", 110, 440, 0.6f, Wave.Saw, 0.4f);
            Make("kill", 660, 990, 0.18f, Wave.Triangle, 0.4f);
            Make("cap", 440, 880, 0.4f, Wave.Sine, 0.4f);
            Make("win", 330, 660, 0.9f, Wave.Triangle, 0.45f);
            Make("lose", 330, 110, 0.9f, Wave.Triangle, 0.45f);
            Make("tap", 900, 700, 0.04f, Wave.Sine, 0.25f);
        }

        void Make(string name, float f0, float f1, float dur, Wave wave, float vol)
        {
            const int rate = 44100;
            int n = Mathf.Max(1, (int)(rate * dur));
            var data = new float[n];
            double phase = 0;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)n;
                float f = f0 * Mathf.Pow(f1 / f0, t);
                phase += f / rate;
                float p = (float)(phase - System.Math.Floor(phase));
                float x;
                switch (wave)
                {
                    case Wave.Sine: x = Mathf.Sin(p * Util.Tau); break;
                    case Wave.Triangle: x = 1f - 4f * Mathf.Abs(p - 0.5f); break;
                    case Wave.Square: x = p < 0.5f ? 1f : -1f; break;
                    default: x = 2f * p - 1f; break;
                }
                float env = (1f - t) * (1f - t) * Mathf.Min(1f, i / 200f);
                data[i] = x * env * vol;
            }
            var clip = AudioClip.Create(name, n, 1, rate, false);
            clip.SetData(data, 0);
            clips[name] = clip;
        }

        public void Play(string name)
        {
            if (Muted) return;
            if (clips.TryGetValue(name, out var c)) src.PlayOneShot(c);
        }
    }
}
