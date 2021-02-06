﻿namespace UglyToad.PdfPig.Images.Png
{
    using Content;
    using Graphics.Colors;

    internal static class PngFromPdfImageFactory
    {
        public static bool TryGenerate(IPdfImage image, out byte[] bytes)
        {
            bytes = null;

            var hasValidDetails = image.ColorSpaceDetails != null &&
                                  !(image.ColorSpaceDetails is UnsupportedColorSpaceDetails);
            var actualColorSpace = hasValidDetails ? image.ColorSpaceDetails.BaseType : image.ColorSpace;

            var isColorSpaceSupported =
                actualColorSpace == ColorSpace.DeviceGray || actualColorSpace == ColorSpace.DeviceRGB;

            if (!isColorSpaceSupported || !image.TryGetBytes(out var bytesPure))
            {
                return false;
            }

            bytesPure = ColorSpaceDetailsByteConverter.Convert(image.ColorSpaceDetails, bytesPure);

            try
            {
                var is3Byte = actualColorSpace == ColorSpace.DeviceRGB;
                var multiplier = is3Byte ? 3 : 1;

                var builder = PngBuilder.Create(image.WidthInSamples, image.HeightInSamples, false);

                var isCorrectlySized = bytesPure.Count == (image.WidthInSamples * image.HeightInSamples * (image.BitsPerComponent / 8) * multiplier);

                if (!isCorrectlySized)
                {
                    return false;
                }

                var i = 0;
                for (var y = 0; y < image.HeightInSamples; y++)
                {
                    for (var x = 0; x < image.WidthInSamples; x++)
                    {
                        if (is3Byte)
                        {
                            builder.SetPixel(bytesPure[i++], bytesPure[i++], bytesPure[i++], x, y);
                        }
                        else
                        {
                            var pixel = bytesPure[i++];
                            builder.SetPixel(pixel, pixel, pixel, x, y);
                        }
                    }
                }

                bytes = builder.Save();

                return true;
            }
            catch
            {
                // ignored.
            }

            return false;
        }
    }
}
