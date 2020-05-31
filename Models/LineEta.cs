using System;
namespace moovit_interview
{
    public class LineEta
    {
        public string LineNumber { get; private set; }
        public DateTime Eta { get; private set; }

        public LineEta(string lineNumber, DateTime eta)
        {
            LineNumber = lineNumber;
            Eta = eta;
        }
    }
}