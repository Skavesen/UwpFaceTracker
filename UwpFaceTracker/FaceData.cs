using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UwpFaceTracker
{
    public class FaceData
    {
        public double X { get; set; }

        public double Y { get; set; }

        public double WidthHeight { get; set; }

        public string base64image { get; set; }

        public List<FaceData> _faceDataList;
    }
}
