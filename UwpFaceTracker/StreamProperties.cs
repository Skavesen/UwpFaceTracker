using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Media.MediaProperties;

namespace UwpFaceTracker
{
    public class StreamProperties
    {
        public StreamProperties(IMediaEncodingProperties properties)
        {
            if (properties == null)
            {
                throw new ArgumentNullException(nameof(properties));
            }

            // This helper class only uses VideoEncodingProperties or VideoEncodingProperties
            if (!(properties is ImageEncodingProperties) && !(properties is VideoEncodingProperties))
            {
                throw new ArgumentException("Argument is of the wrong type. Required: " + typeof(ImageEncodingProperties).Name
                    + " or " + typeof(VideoEncodingProperties).Name + ".", nameof(properties));
            }

            // Store the actual instance of the IMediaEncodingProperties for setting them later
            EncodingProperties = properties;
        }

        public uint Width
        {
            get
            {
                if (EncodingProperties is ImageEncodingProperties)
                {
                    return (EncodingProperties as ImageEncodingProperties).Width;
                }
                else if (EncodingProperties is VideoEncodingProperties)
                {
                    return (EncodingProperties as VideoEncodingProperties).Width;
                }

                return 0;
            }
        }

        public uint Height
        {
            get
            {
                if (EncodingProperties is ImageEncodingProperties)
                {
                    return (EncodingProperties as ImageEncodingProperties).Height;
                }
                else if (EncodingProperties is VideoEncodingProperties)
                {
                    return (EncodingProperties as VideoEncodingProperties).Height;
                }

                return 0;
            }
        }

        public uint FrameRate
        {
            get
            {
                if (EncodingProperties is VideoEncodingProperties)
                {
                    if ((EncodingProperties as VideoEncodingProperties).FrameRate.Denominator != 0)
                    {
                        return (EncodingProperties as VideoEncodingProperties).FrameRate.Numerator /
                            (EncodingProperties as VideoEncodingProperties).FrameRate.Denominator;
                    }
                }

                return 0;
            }
        }

        public double AspectRatio
        {
            get { return Math.Round((Height != 0) ? (Width / (double)Height) : double.NaN, 2); }
        }

        public IMediaEncodingProperties EncodingProperties { get; }

        public string GetFriendlyName(bool showFrameRate = true)
        {
            if (EncodingProperties is ImageEncodingProperties ||
                !showFrameRate)
            {
                return Width + "x" + Height + " [" + AspectRatio + "] " + EncodingProperties.Subtype;
            }
            else if (EncodingProperties is VideoEncodingProperties)
            {
                return Width + "x" + Height + " [" + AspectRatio + "] " + FrameRate + "FPS " + EncodingProperties.Subtype;
            }

            return String.Empty;
        }
    }
}
