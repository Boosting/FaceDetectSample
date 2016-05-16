using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.IO;
using System.Windows.Media.Imaging;
using NNTrainer;

using SIFT_M;

namespace GenPositionData
{
    /// <summary>
    /// Provides base services for face Reco and eyeReco classes
    /// </summary>
    public class BaseReco
    {
        public enum NormalizeActionsEnum { None, ConstantSum, BlurSubtract, BlurSubtract3, SIFT };
        protected double _faceDisplayWidth = 41;
        protected double _faceDisplayHeight = 41;
        protected int _normalizeSum;
        protected int _defaultNormalizedLen;
        protected int _dataBytePerPixel;
        protected int _blurKernelSize = 9;
        protected float _blurVar = 5.0F;

        protected BitmapSource _bitmap;
        protected double _defaultDPI = 96;
        protected TrainDataFileWriter.ModeEnum _dataFileMode = TrainDataFileWriter.ModeEnum.Text;
        protected NormalizeActionsEnum _normalizeAction = NormalizeActionsEnum.ConstantSum;

        #region SIFT Parameters
        protected int _partitionNumber = 4;//better set it to be the square of an integer
               
        protected CSIFTInterestPointDescriptorM _siftDesc = null;
        #endregion SIFT Parameters

        public byte [] Normalize(byte [] data, Rect rect, int bytePerPixel)
        {
            byte [] ret;
            switch (_normalizeAction)
            {
                case NormalizeActionsEnum.ConstantSum:
                    ret =  NormalizeBySum(data);
                    break;

                case NormalizeActionsEnum.BlurSubtract:
                    ret = NormalizeByBlur(data, rect, bytePerPixel, this._blurKernelSize, this._blurVar);
                    break;

                case NormalizeActionsEnum.BlurSubtract3:
                    ret = NormalizeByBlur3(data, rect, bytePerPixel, this._blurKernelSize, this._blurVar);
                    break;

                case NormalizeActionsEnum.None:
                    ret = data;
                    break;

                case NormalizeActionsEnum.SIFT:
                    ret = NormalizeBySIFT(data, rect, bytePerPixel);
                    break;
                default:
                    ret =  NormalizeBySum(data);
                    break;
            }

            return ret;
        }

        public byte[] NormalizeBySIFT(byte[] data, Rect rect, int bytePerPix)
        {
            if (this._siftDesc == null)
            {
                SIFTParametersM param = new SIFTParametersM();
                this._siftDesc = new CSIFTInterestPointDescriptorM();
                this._siftDesc.GetDefaultParameters(param);
                this._siftDesc.Create(param);
            }

            //Convert the byte array into images
            CFloatImgM Img = new CFloatImgM(Convert.ToInt32(rect.Width), Convert.ToInt32(rect.Height), 1);
            int index = 0;
            for (int i = 0; i < rect.Height; i++)
            {
                for (int j = 0; j < rect.Width; j++)
                {
                    Img.set_m_Pix(i, j, data[index++]);
                }
            }

            InterestPointM interestPt = new InterestPointM();

            int partRowNum = Convert.ToInt32(Math.Sqrt((double)_partitionNumber));
            int partColNum = partRowNum;

            byte[] ret = new byte[partRowNum * partColNum * 128];

            float partWidth = (float)rect.Width / (float)partColNum;
            float partHeight = (float)rect.Height / (float)partRowNum;

            interestPt.fAngle = 0;
            interestPt.fStrength = 100;
            interestPt.SetScale((float)partWidth/16.0f);

            this._siftDesc.Update(Img);

            index = 0;
            List<byte> siftDescriptor=new List<byte>();
            bool descriptor = true;

            for (int i = 0; i < partRowNum; i++)
            {                
                for (int j = 0; j < partColNum; j++)
                {
                    interestPt.SetPosition((j + 0.5f) * partWidth, (i + 0.5f) * partHeight);
                    siftDescriptor.Clear();
                    this._siftDesc.GetDescriptorUChar(interestPt, siftDescriptor, ref descriptor);
                    foreach (byte val in siftDescriptor)
                    {
                        ret[index++] = val;
                    }
                }
            }
            return ret;
        }

        public byte[] NormalizeByBlur(byte[] dataPixs, Rect rect, int bytePerPix, int kernelSize, float blurVar)
        {
            Dpu.ImageProcessing.Image blur = new Dpu.ImageProcessing.Image(kernelSize, kernelSize);
            Dpu.ImageProcessing.Image.Blur_Kernel(blur, 1.0F, blurVar, blurVar, true);

            Dpu.ImageProcessing.Image[] src = LibFaceData.ImageUtils.ConvertByteArrayToImageArray(dataPixs, rect, bytePerPix);
            Dpu.ImageProcessing.Image dest = new Dpu.ImageProcessing.Image((int)rect.Width, (int)rect.Height);
            Dpu.ImageProcessing.Image.ConvolutionReflecting(src[0], 1, 1, blur, dest);
            Dpu.ImageProcessing.Image.Subtract(src[0], dest, dest);
            Dpu.ImageProcessing.Image.NormalizeMeanStdev(dest, 128, 32, dest);
            Dpu.ImageProcessing.Image[] destArray = new Dpu.ImageProcessing.Image[1];

            destArray[0] = dest;
            byte[] ret = LibFaceData.ImageUtils.ConvertImageArrayToByteArray(destArray);

            return ret;
        }

        public byte[] NormalizeByBlur3(byte[] dataPixs, Rect rect, int bytePerPix, int kernelSize, float blurVar)
        {
            Dpu.ImageProcessing.Image blur = new Dpu.ImageProcessing.Image(kernelSize, kernelSize);
            Dpu.ImageProcessing.Image.Blur_Kernel(blur, 1.0F, blurVar, blurVar, true);

            Dpu.ImageProcessing.Image[] src = LibFaceData.ImageUtils.ConvertByteArrayToImageArray(dataPixs, rect, bytePerPix);

            byte[] ret = new byte[(int)rect.Width * (int)rect.Height * (bytePerPix-1)];
            for (int ib = 1; ib < bytePerPix; ++ib)
            {
                Dpu.ImageProcessing.Image.Divide(src[ib], src[0], src[ib]);
            }
            int destOff = 0;

            Dpu.ImageProcessing.Image dest = new Dpu.ImageProcessing.Image((int)rect.Width, (int)rect.Height);
            Dpu.ImageProcessing.Image[] destArray = new Dpu.ImageProcessing.Image[1];

            for (int ib = 1; ib < bytePerPix; ++ib)
            {
                Dpu.ImageProcessing.Image.ConvolutionReflecting(src[ib], 1, 1, blur, dest);
                //Dpu.ImageProcessing.Image.Subtract(src[ib], dest, dest);
                Dpu.ImageProcessing.Image.NormalizeMeanStdev(dest, 128, 32, dest);
                destArray[0] = dest;
                //destArray[0] = src[ib];
                byte[] tmp = LibFaceData.ImageUtils.ConvertImageArrayToByteArray(destArray);
                Array.Copy(tmp, 0, ret, destOff, tmp.Length);
                destOff += tmp.Length;
            }

            return ret;
        }

        public byte[] NormalizeBySum(byte[] facePix)
        {
            int sum = 0;

            for (int iPix = 0; iPix < facePix.Length; ++iPix)
            {
                sum += facePix[iPix];
            }

            int normalizeSum = this._normalizeSum * facePix.Length / this._defaultNormalizedLen;

            for (int iPix = 0; iPix < facePix.Length; ++iPix)
            {
                int pix = facePix[iPix] * normalizeSum / sum;
                facePix[iPix] = (byte)(Math.Min(byte.MaxValue, pix));
            }

            return facePix;
        }

        public byte[] ConvertToGreyScale(byte[] facePix)
        {
            int cColorPlane = (int)(facePix.Length / this._faceDisplayWidth / this._faceDisplayHeight);

            return ConvertToGreyScale(facePix, cColorPlane);
        }

        public byte[] ConvertToGreyScale(byte[] facePix, int cColorPlane)
        {
            byte[] grey = new byte[facePix.Length / cColorPlane];

            int iGrey = 0;

            for (int i = 0; i < facePix.Length; )
            {
                int val = 0;
                for (int j = 0; j < cColorPlane; ++j)
                {
                    val += facePix[i];
                    ++i;
                }
                grey[iGrey++] = (byte)(val / cColorPlane);
            }

            return grey;
        }

        public byte[] SelectAndNormalizePatch(byte[] dataPixs, Point sourceLeft, Point sourceRight, Point targetLeft, Point targetRight, Rect targetRect)
        {

            int stride = this._bitmap.PixelWidth * this._dataBytePerPixel;
            Rect sourceRect = new Rect(0, 0, this._bitmap.PixelWidth, this._bitmap.PixelHeight);

            byte[] facePixs = null;

            Dpu.ImageProcessing.Image[] images = LibFaceData.ImageUtils.ConvertByteArrayToImageArray(dataPixs, sourceRect, this._dataBytePerPixel);

            images = LibFaceData.ImageUtils.ExtractNormalizeFace(images, sourceRect,
                                            sourceLeft, sourceRight,
                                            this._dataBytePerPixel, targetRect,
                                            targetLeft, targetRight);

            facePixs = LibFaceData.ImageUtils.ConvertImageArrayToByteArray(images);
            //stride = (int)(faceRect.Width * _dataBytePerPixel);
            //BitmapSource ret = (BitmapSource)BitmapImage.Create((int)faceRect.Width, (int)faceRect.Height,
            //    this._defaultDPI,
            //    this._defaultDPI,
            //    this._bitmap.Format, null, facePixs, stride);


            return facePixs;

        }

        public byte[] CreateMainBitMap(string filename)
        {
            Uri uri = new Uri("file:" + filename);
            if (false == System.IO.File.Exists(uri.LocalPath))
            {
                throw new Exception("Cannot find photo file " + filename);
            }

            BitmapImage bitmapImage = new BitmapImage();
            bitmapImage.BeginInit();
            bitmapImage.UriSource = uri;
            bitmapImage.EndInit();

            if (System.Windows.Media.PixelFormats.Rgb24 != bitmapImage.Format)
            {
                this._bitmap = new FormatConvertedBitmap(bitmapImage, System.Windows.Media.PixelFormats.Rgb24, null, 0.0) as BitmapSource;
            }
            else
            {
                this._bitmap = bitmapImage as BitmapSource;
            }

            if (null == this._bitmap)
            {
                throw new Exception("Bitmap is null");
            }

            this._dataBytePerPixel = this._bitmap.Format.BitsPerPixel / 8;
            int stride = this._bitmap.PixelWidth * this._dataBytePerPixel;
            byte[] dataPixs = new byte[stride * this._bitmap.PixelHeight];
            this._bitmap.CopyPixels(dataPixs, stride, 0);

            if (this._bitmap.DpiX != this._defaultDPI ||
                this._bitmap.DpiY != this._defaultDPI)
            {
                this._bitmap = BitmapImage.Create(this._bitmap.PixelWidth, this._bitmap.PixelHeight,
                    this._defaultDPI, this._defaultDPI,
                    this._bitmap.Format,
                    null,
                    dataPixs, stride);
            }


            return dataPixs;
        
        }
        /// <summary>
        /// Convert a normalized point position to an absolute position
        /// given the rect used for nomalization
        /// </summary>
        /// <param name="point">Normalized point</param>
        /// <param name="rect">ARectangle</param>
        /// <returns>Absolute point location</returns>
        public Point NormalizePosToAbs(Point point, Rect rect)
        {
            Point absPoint = new Point();

            absPoint.X = rect.Left + point.X * rect.Width;
            absPoint.Y = rect.Top + point.Y * rect.Height;

            return absPoint;
        }
        public void SaveAsJpeg(byte[] pixs, Rect rect, string file, string keyword, System.Windows.Media.PixelFormat fmt, int bytePerPixel = -1)
        {
            int pixCount = (int)(rect.Width * rect.Height);
            if (pixCount <= 0)
            {
                return;
            }

            int dataPerPix = pixs.Length / pixCount;
            if (bytePerPixel  < 0)
            {
                bytePerPixel = this._dataBytePerPixel;
            }
            int stride = (int)rect.Width * bytePerPixel;

            BitmapSource image = BitmapSource.Create((int)rect.Width, (int)rect.Height,
                    this._defaultDPI, this._defaultDPI,
                    //this._bitmap.Format,
                    fmt,
                    null,
                    pixs, stride);

            JpegBitmapEncoder encoder = new JpegBitmapEncoder();
            encoder.QualityLevel = 50;
            encoder.Frames.Add(BitmapFrame.Create(image));
            //if (null != keyword)
            //{
            //    List<string> keywordList = new List<string>();
            //    keywordList.Add(keyword);
            //    System.Collections.ObjectModel.ReadOnlyCollection<string> ll = new System.Collections.ObjectModel.ReadOnlyCollection<string>(keywordList);

            //    BitmapMetadata meta = new BitmapMetadata("jpg");
            //    meta.Keywords = ll;
            //    encoder.Metadata = meta;
            //}
            using (FileStream fs = new FileStream(file, FileMode.Create))
            {
                encoder.Save(fs);
            }

        }
        public byte[] MakeFaceData(byte[] facePix)
        {
            byte[] data;

            int cColorPlane = (int)(facePix.Length / this._faceDisplayWidth / this._faceDisplayHeight);

            if (cColorPlane > 1)
            {
                // Reorder the pixels by color plane
                data = new byte[facePix.Length];

                int iPix = 0;
                for (int iColor = 0; iColor < cColorPlane; ++iColor)
                {
                    for (int i = 0; i < facePix.Length; i += cColorPlane)
                    {
                        data[iPix++] = facePix[i + iColor];
                    }
                }
            }
            else
            {
                data = facePix;
            }

            return data;
        }
    }
}
