namespace RtMidiRecorder.Midi.Extensions;

public static class MathExtensions
{
   public static double Median(this IEnumerable<double> source)
   {
      var doubleArr = source as double[] ?? source.ToArray();

      if (doubleArr.Count() is not (var doubles and > 0))
         throw new InvalidOperationException("Sequence contains no elements");

      var midpoint = (doubles - 1) / 2;
      var sorted = doubleArr.OrderBy(n => n).AsQueryable();
      var median = sorted.ElementAt(midpoint);

      if (doubles % 2 == 0)
         median = (median + sorted.ElementAt(midpoint + 1)) / 2;

      return median;
   }
}