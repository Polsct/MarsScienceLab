// Reusable work buffers for the original four-constraint MLE thrust mixer.
// Pure arithmetic so the flight implementation can be regression-tested off-line.
using System;

namespace MSLSkyCrane
{
    public sealed class DescentMixer
    {
        readonly int count;
        readonly double[,] arms, work;
        readonly double[] rhs, delta;
        public readonly float[] Weights;

        public DescentMixer(int count)
        {
            if (count < 1) throw new ArgumentOutOfRangeException("count");
            this.count = count;
            arms = new double[4, count]; work = new double[4, 4];
            rhs = new double[4]; delta = new double[count]; Weights = new float[count];
            for (int i = 0; i < count; i++) arms[3, i] = 1;
        }

        public void SetArm(int i, double x, double y, double z)
        { arms[0, i] = x; arms[1, i] = y; arms[2, i] = z; }

        public void Mix(double x, double y, double z, double magnitude, double authority, double maxSplit)
        {
            for (int r = 0; r < 4; r++)
                for (int s = 0; s < 4; s++)
                {
                    double sum = 0;
                    for (int i = 0; i < count; i++) sum += arms[r, i] * arms[s, i];
                    work[r, s] = sum + (r == s ? 1e-9 : 0);
                }
            rhs[0] = x; rhs[1] = y; rhs[2] = z; rhs[3] = 0;
            for (int c = 0; c < 4; c++)
            {
                int p = c;
                for (int r = c + 1; r < 4; r++) if (Math.Abs(work[r, c]) > Math.Abs(work[p, c])) p = r;
                for (int s = 0; s < 4; s++)
                { double t = work[c, s]; work[c, s] = work[p, s]; work[p, s] = t; }
                { double t = rhs[c]; rhs[c] = rhs[p]; rhs[p] = t; }
                for (int r = 0; r < 4; r++)
                {
                    if (r == c) continue;
                    double f = work[r, c] / work[c, c];
                    for (int s = c; s < 4; s++) work[r, s] -= f * work[c, s];
                    rhs[r] -= f * rhs[c];
                }
            }
            for (int c = 0; c < 4; c++) rhs[c] /= work[c, c];
            double dmax = 1e-9;
            for (int i = 0; i < count; i++)
            {
                delta[i] = arms[0, i] * rhs[0] + arms[1, i] * rhs[1] + arms[2, i] * rhs[2] + arms[3, i] * rhs[3];
                dmax = Math.Max(dmax, Math.Abs(delta[i]));
            }
            double mag = Math.Min(1.0, magnitude) * authority;
            double scale = maxSplit * mag / dmax;
            float total = 0;
            for (int i = 0; i < count; i++)
            {
                Weights[i] = Math.Max(0.002f, (float)(1.0 + delta[i] * scale) / count);
                total += Weights[i];
            }
            for (int i = 0; i < count; i++) Weights[i] /= total;
        }
    }
}
