namespace OldColoredKeycards.Keycards;

internal static class SpawnWeightSolver
{
    private const int Samples = 2000;
    private const int Rounds = 3;

        public static double[] Solve(double[] poolWeights, int guaranteed, double mean, double std, double multiplier, double[] targets, int seed)
    {
        var random = new Random(seed);
        var weights = new double[targets.Length];

        for (var round = 0; round < Rounds; round++)
        {
            var thresholds = Thresholds(poolWeights, weights, guaranteed, mean, std, multiplier, random);
            for (var i = 0; i < targets.Length; i++)
            {
                weights[i] = SolveWeight(thresholds, targets[i]);
            }
        }

        return weights;
    }

    private static double[] Thresholds(double[] poolWeights, double[] extraWeights, int guaranteed, double mean, double std, double multiplier, Random random)
    {
        var weights = poolWeights.Concat(extraWeights.Where(weight => weight > 0)).ToArray();
        var keys = new double[weights.Length];
        var thresholds = new double[Samples];
        for (var sample = 0; sample < Samples; sample++)
        {
            var draws = (int)Math.Round(multiplier * Normal(mean, std, random)) - guaranteed;
            if (draws <= 0)
            {
                thresholds[sample] = -1;
                continue;
            }

            if (draws >= weights.Length)
            {
                thresholds[sample] = double.PositiveInfinity;
                continue;
            }

            for (var i = 0; i < weights.Length; i++)
            {
                keys[i] = weights[i] > 0 ? -Math.Log(1.0 - random.NextDouble()) / weights[i] : double.PositiveInfinity;
            }

            thresholds[sample] = QuickSelect(keys, draws - 1, random);
        }

        return thresholds;
    }

    private static double PickChance(double weight, double[] thresholds)
    {
        var total = 0.0;
        foreach (var threshold in thresholds)
        {
            total += threshold < 0 ? 0 : double.IsPositiveInfinity(threshold) ? 1 : 1 - Math.Exp(-weight * threshold);
        }

        return total / thresholds.Length;
    }

    private static double SolveWeight(double[] thresholds, double target)
    {
        if (target <= 0)
        {
            return 0;
        }

        double low = 0, high = 1e-6;
        while (PickChance(high, thresholds) < target && high < 1e6)
        {
            high *= 2;
        }

        for (var i = 0; i < 60; i++)
        {
            var middle = (low + high) / 2;
            if (PickChance(middle, thresholds) < target)
            {
                low = middle;
            }
            else
            {
                high = middle;
            }
        }

        return (low + high) / 2;
    }

    private static double Normal(double mean, double std, Random random)
    {
        for (var attempt = 0; attempt <= 100; attempt++)
        {
            double u = 0, v = 0;
            while (u == 0)
            {
                u = random.NextDouble();
            }

            while (v == 0)
            {
                v = random.NextDouble();
            }

            var value = mean + Math.Sqrt(-2 * Math.Log(u)) * Math.Cos(2 * Math.PI * v) * std;
            if (value >= 0)
            {
                return value;
            }
        }

        return 0.01 + random.NextDouble() * (mean * 2 - 0.01);
    }

    private static double QuickSelect(double[] values, int k, Random random)
    {
        int low = 0, high = values.Length - 1;
        while (low < high)
        {
            var pivot = values[low + random.Next(high - low + 1)];
            int i = low, j = high;
            while (i <= j)
            {
                while (values[i] < pivot)
                {
                    i++;
                }

                while (values[j] > pivot)
                {
                    j--;
                }

                if (i <= j)
                {
                    (values[i], values[j]) = (values[j], values[i]);
                    i++;
                    j--;
                }
            }

            if (k <= j)
            {
                high = j;
            }
            else if (k >= i)
            {
                low = i;
            }
            else
            {
                return values[k];
            }
        }

        return values[low];
    }
}
