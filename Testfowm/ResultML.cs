using System;
using System.Collections.Generic;
using System.Text;

namespace Testfowm
{
    class ResultML
    {
        public string Label { get; set; }

        public float Confidence { get; set; }

        public float X { get; set; }

        public float Y { get; set; }

        public float Width { get; set; }

        public float Height { get; set; }
    }
}
