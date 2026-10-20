namespace OldColoredKeycards.Keycards;

// Works out the probability (weight) each restored 0.14 spot needs so it gives its 0.14 number of keycards per
// raid on this server's actual pool, whatever other mods added to or replaced in it.
//
// SPT's loose loot draw (LocationLootGenerator.GenerateDynamicLoot): N = round(multiplier * Normal(mean, std))
// minus the spots with probability 1; then N spots are drawn by weight without replacement. A weighted draw
// without replacement picks the same spots as an "exponential race": each spot gets the key Exp(1) / weight and
// the N smallest keys win. So in one simulated raid, with T the N-th smallest key, a spot of weight w is picked
// with chance 1 - exp(-w * T). Averaging that over many simulated raids gives its pick chance.
// Tools/Calibrate uses the same model.
internal static class SpawnWeightSolver
{
    private const int Samples = 2000;
    private const int Rounds = 3;

    /// <param name="poolWeights">Probabilities of the spots SPT draws from (not probability 1, not always-spawn, not blacklisted).</param>
    /// <param name="guaranteed">Spots with probability exactly 1. They take draws first.</param>
    /// <param name="targets">Keycards per raid wanted from each restored spot.</param>
    public static double[] Solve(double[] poolWeights, int guaranteed, double mean, double std, double multiplier, double[] targets, int seed)
    {
        var random = new Random(seed);
        var weights = new double[targets.Length];

        // The restored spots join the pool too, so solve, put them in, and solve again.
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

    // The N-th smallest key in each simulated raid. -1 = nothing drawn, infinity = every spot drawn.
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

    // Pick chance only grows with weight, so a bisection finds it. 0 if the target is 0.
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

    // RandomUtil.GetNormallyDistributedRandomNumber: negative draws are re-rolled, 100 tries, then uniform.
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

    // The k-th smallest value (0-based). Reorders the array.
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
