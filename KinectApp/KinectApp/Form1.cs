using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Emgu.Util;
using Microsoft.Kinect;
using Emgu.CV.UI;
using Emgu.CV.VideoSurveillance;
using System.IO;
using System.Globalization;

namespace KinectApp
{
    public partial class KinectApp : Form
    {
        private KinectSensor kinectSensor = null;

        private ColorFrameReader colorFrameReader = null;
        private DepthFrameReader depthFrameReader = null;
        private CoordinateMapper coordinateMapper = null;


        Image<Gray, Byte> binImage;
        Image<Gray, Byte> crImage;
        Image<Rgb, Byte> colorImage;
        Image<Gray, Byte> imageToDisplay;
        private byte[] colorPixels;
        private byte[] binaryPixels;
        private ushort[] uDepthPixels;
        private DepthSpacePoint[] mappedDepth;


        private const int colorWidth = 1920;
        private const int colorHeight = 1080;
        private const int binWidth = 960;
        private const int binHeight = 540;
        private const int depthWidth = 512;
        private const int depthHeight = 424;

        //Size of the window used for the median filter
        private int windowSize = 5;

        private const int MapDepthToByte = 8000 / 256;
        private int threshold = 160;
        private const byte max = (byte)255;
        private const byte min = (byte)0;

        private int r;
        private int g;
        private int b;
        private double cr;

        //Camera constants
        private const double alpha_u = 1073.48539;
        private const double alpha_v = 1077.91738;
        private const double u0 = 970.95975;
        private const double v0 = 515.18869;

        //Temporary
        string path = @"C:\Users\near\Desktop\PFE\data\Kinect_Presicion";
        string writingLocation = null;

        string dataContainer  = "";
        bool isWriting = false;
        byte[] outputImage;

        int grayLevel = 0;
        MCvFont coordFont;
        MCvFont textFont;


        private enum Image_Modes
        {
            binary,
            grayscale
        }
        private Image_Modes MODE = Image_Modes.grayscale;

        public KinectApp()
        {
            InitializeComponent();
            kinectSensor = KinectSensor.GetDefault();

            this.KeyPreview = true;
            this.KeyDown += KinectApp_KeyDown;

            var frameDesc = kinectSensor.ColorFrameSource.CreateFrameDescription(ColorImageFormat.Rgba);
            colorPixels = new byte[frameDesc.BytesPerPixel * frameDesc.LengthInPixels];
            binaryPixels = new byte[binHeight * binWidth];
            uDepthPixels = new ushort[depthHeight * depthWidth];
            mappedDepth = new DepthSpacePoint[colorHeight * colorWidth];
            outputImage = new byte[1920*1080/2];

            colorFrameReader = kinectSensor.ColorFrameSource.OpenReader();
            depthFrameReader = kinectSensor.DepthFrameSource.OpenReader();

            binImage = new Image<Gray, byte>(binWidth, binHeight);
            crImage = new Image<Gray, byte>(binWidth, binHeight);
            colorImage = new Image<Rgb, byte>(binWidth, binHeight);
            imageToDisplay = new Image<Gray, byte>(binWidth, binHeight);
            imageToDisplay.Ptr = crImage.Ptr;

            this.colorFrameReader = kinectSensor.ColorFrameSource.OpenReader();
            this.depthFrameReader = kinectSensor.DepthFrameSource.OpenReader();

            this.colorFrameReader.FrameArrived += this.Reader_ColorFrameArrived;
            this.depthFrameReader.FrameArrived += this.Reader_FrameArrived;
            coordinateMapper = kinectSensor.CoordinateMapper;

            coordFont = new MCvFont(FONT.CV_FONT_HERSHEY_DUPLEX, 1, 1);
            textFont = new MCvFont(FONT.CV_FONT_HERSHEY_SIMPLEX, 0.5, 0.5);
            label1.Text = "Threshold: " + threshold.ToString();

            try
            {
                if (!kinectSensor.IsOpen)
                {
                    kinectSensor.Open();
                }

            }
            catch (IOException)
            {
                kinectSensor = null;
            }
            if (!kinectSensor.IsAvailable)
            {

                StatusLabel.ForeColor = Color.Red;
                StatusLabel.Text = "Kinect not connected";
                Bitmap bitmap = new Bitmap(Image.FromFile("nosignal.jpg"));
                Image<Rgb, byte> image = new Image<Rgb, byte>(bitmap);
                imageBox1.Image = image;
            }

            else
            {
                StatusLabel.ForeColor = Color.Green;
                StatusLabel.Text = "Kinect connected";
            }

        }

        private void KinectApp_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.B)
            {
                imageToDisplay.Ptr = binImage.Ptr;
                grayLevel = 255;
            }
            if (e.KeyCode == Keys.G)
            {
                imageToDisplay.Ptr = crImage.Ptr;
                grayLevel = 0;
            }
        }

        private void Reader_FrameArrived(object sender, DepthFrameArrivedEventArgs e)
        {
            using (DepthFrame depthFrame = e.FrameReference.AcquireFrame())
            {
                if (depthFrame != null)
                {

                    using (Microsoft.Kinect.KinectBuffer depthBuffer = depthFrame.LockImageBuffer())
                    {
                        depthFrame.CopyFrameDataToArray(uDepthPixels);
                    }
                }

            }
            DoMapping();
            
        }

        private void Reader_ColorFrameArrived(object sender, ColorFrameArrivedEventArgs e)
        {
            using (ColorFrame colorFrame = e.FrameReference.AcquireFrame())
            {
                if (colorFrame != null)
                {
                    colorFrame.CopyConvertedFrameDataToArray(this.colorPixels, ColorImageFormat.Rgba);
                    ProcessColor();
                }
            }
        }

        private byte ClipToByte(int p_ValueToClip)
        {
            return Convert.ToByte((p_ValueToClip < byte.MinValue) ? byte.MinValue : ((p_ValueToClip > byte.MaxValue) ? byte.MaxValue : p_ValueToClip));
        }

        private unsafe void ProcessDepthFrameData(IntPtr depthFrameData, uint size, ushort minDepth, ushort maxDepth)
        {
            ushort* frameData = (ushort*)depthFrameData;

            for (int i = 0; i < depthHeight * depthWidth; ++i)
            {
                uDepthPixels[i] = frameData[i];
            }
        }

        unsafe void ProcessColor()
        {
            int k = 0;
            byte* p = (byte*)binImage.MIplImage.imageData;
            byte* q = (byte*)crImage.MIplImage.imageData;
            byte[,,] data = colorImage.Data;

            int l = 0;
            int i_r = 0;
            int j_r = 0;

            for (int j = 0; j < colorHeight; j += 2)
            {
                for (int i = 0; i < colorWidth; i += 2)
                {
                    r = colorPixels[k];
                    g = colorPixels[k + 1];
                    b = colorPixels[k + 2];
                    /*
                    data[j_r, i_r, 0] = colorPixels[k];
                    data[j_r, i_r, 1] = colorPixels[k + 1];
                    data[j_r, i_r, 2] = colorPixels[k + 2];
                    */
                    cr = 0.5 * r - 0.4187 * g - 0.0813 * b + 128;
                    p[i / 2] = (cr > threshold) ? max : min;
  //                  p[i / 2] = outputImage[l];
                    l++;
                    q[i / 2] = (byte)r;
                    k += 8;
                    i_r++;
                }
                j_r++;
                i_r = 0;
                p += binWidth;
                q += binWidth;
                k += colorWidth * 4;
            }
            //            CvInvoke.cvThreshold(binImage.Ptr, binImage.Ptr, threshold, 255, THRESH.CV_THRESH_BINARY);
            DetectContours();
            imageBox1.Image = imageToDisplay;
        }
        private void DrawText(string text, int x, int y, MCvFont font)
        {
            CvInvoke.cvPutText(imageToDisplay.Ptr,
                    text,
                    new Point(x, y),
                    ref font,
                    new Gray(grayLevel).MCvScalar);

        }
        private void DetectContours()
        {
            using (MemStorage stor = new MemStorage())
            {
                //Find contours with no holes try CV_RETR_EXTERNAL to find holes
                Contour<System.Drawing.Point> contours = binImage.FindContours(
                 Emgu.CV.CvEnum.CHAIN_APPROX_METHOD.CV_LINK_RUNS,
                 Emgu.CV.CvEnum.RETR_TYPE.CV_RETR_CCOMP,
                 stor);

                int objectCount = 0;
                for (; contours != null; contours = contours.HNext)
                {
                    MCvBox2D box = contours.GetMinAreaRect();
                    if (contours.Area > 20)
                    {
                        float centerX = box.center.X;
                        float centerY = box.center.Y;
                        imageToDisplay.Draw(box.MinAreaRect(), new Gray(200), 2);
                        imageToDisplay.Draw(new CircleF(box.center, 2), new Gray(0), 2);
                        int x = (int)(centerX + 0.5) * 2;
                        int y = (int)(centerY + 0.5) * 2;
                        double depth = ExperimentalDepth(x, y);
                        double rx = GetX(x, depth);
                        double ry = GetY(y, depth);
                        string text = String.Format("{0:F2}, {1:F2}, {2:F2}", rx, ry, depth);

                        //if (isWriting) dataContainer += depth.ToString() + "\n";
                        if (isWriting) dataContainer += text + "\n";

                        DrawText(text, (int)(centerX - box.size.Width / 2), (int)(centerY - box.size.Height), coordFont);
                        objectCount++;
                       
                    }

                }
                DrawText("Objects detected: " + objectCount.ToString(), 20, 20, textFont);
            }
        }

        private void DoMapping() => coordinateMapper.MapColorFrameToDepthSpace(uDepthPixels, mappedDepth);

        private double GetDepth(int x, int y)
        {
            int i = y * colorWidth + x;
            float colorMappedToDepthX = mappedDepth[i].X;
            float colorMappedToDepthY = mappedDepth[i].Y;
            if (!float.IsNegativeInfinity(colorMappedToDepthX) &&
                !float.IsNegativeInfinity(colorMappedToDepthY))
            {
            
                int depthX = (int)(colorMappedToDepthX + 0.5f);
                int depthY = (int)(colorMappedToDepthY + 0.5f);

                if ((depthX >= 0) && (depthX < depthWidth) && (depthY >= 0) && (depthY < depthHeight))
                {
                    int depthIndex = (depthY * depthWidth) + depthX;
                    return uDepthPixels[depthIndex];
                }

            }
            return -1;

        }

        private double ExperimentalDepth(int x, int y)
        {
            double[] depths = new double[windowSize * windowSize];
            int k = 0;
            int start = windowSize / 2;

            for (int i = -start; i <= start; i++)
            {
                for (int j = -start; j <= start; j++)
                {
                    depths[k] = GetDepth(x + i, y + j);
                    k++;
                }
            }
            Array.Sort(depths);
            return depths[(windowSize * windowSize) / 2];
        }
        private double GetX(int px, double z) => z * (px - u0) / alpha_u;
        private double GetY(int py, double z) => z * (py - v0) / alpha_v;

        private void ThresholdSlider_Scroll(object sender, EventArgs e)
        {
            threshold = ThresholdSlider.Value;
            label1.Text = "Threshold: " + threshold.ToString();
        }

        private void textBox1_TextChanged(object sender, EventArgs e)
        {

        }

        private void button1_Click(object sender, EventArgs e)
        {
            WriteData();
            string text = textBox1.Text;
            writingLocation = Path.Combine(path, "Distance_" + text + ".txt");
            isWriting = true;
        }

        private void button2_Click(object sender, EventArgs e)
        {
            isWriting = false;
            WriteData();
        }

        private void WriteData()
        {
            if (writingLocation != null)
            {
                isWriting = false;
                StreamWriter writer = new StreamWriter(writingLocation, true);
                writer.Write(dataContainer);
                writer.Close();
                dataContainer = "";
            }
        }
        
        private void label2_Click(object sender, EventArgs e)
        {

        }

        private void KinectApp_Load(object sender, EventArgs e)
        {

        }

        
    }
}
