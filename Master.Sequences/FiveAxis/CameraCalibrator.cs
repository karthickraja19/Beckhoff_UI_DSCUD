using System;
using System.Collections.Generic;
using System.Linq;
using OpenCvSharp;

namespace Master.Sequences.FiveAxis
{
    /// <summary>What cv2.calibrateCamera returns, for a planar target.</summary>
    public sealed class CameraCalibrationResult
    {
        /// <summary>Overall RMS re-projection error, pixels.</summary>
        public double Rms { get; set; }

        public double Fx { get; set; }

        public double Fy { get; set; }

        public double Cx { get; set; }

        public double Cy { get; set; }

        /// <summary>k1, k2, p1, p2, k3 (OpenCV's order).</summary>
        public double[] Distortion { get; set; }

        /// <summary>Per image: the plate's rotation vector (Rodrigues) and translation in the camera frame, mm.</summary>
        public double[][] Rvecs { get; set; }

        public double[][] Tvecs { get; set; }

        public double[,] CameraMatrix
        {
            get { return new[,] { { Fx, 0, Cx }, { 0, Fy, Cy }, { 0, 0, 1 } }; }
        }
    }

    /// <summary>
    /// SOP Step 1.4: <c>cv2.calibrateCamera</c> over the 34 images of the dot plate (camera matrix K and
    /// distortion D, and each image's plate pose), and <c>cv2.solvePnP</c> for the pose in one image.
    /// Both are OpenCvSharp's own calls.
    /// </summary>
    public static class CameraCalibrator
    {
        /// <param name="obj">The plate's dot positions, mm: [n][x, y] (Z is 0).</param>
        /// <param name="views">Per image the dots' pixel positions in the same order: [view][n][u, v].</param>
        public static CameraCalibrationResult Calibrate(double[][] obj, IList<double[][]> views, int width, int height)
        {
            if (obj == null || obj.Length < 6)
                throw new ArgumentException("The plate needs at least 6 points.");
            if (views == null || views.Count < 3)
                throw new ArgumentException("At least 3 images are needed to calibrate the camera.");

            Point3f[] objectPoints = obj.Select(p => new Point3f((float)p[0], (float)p[1], 0f)).ToArray();
            var objectList = new List<IEnumerable<Point3f>>();
            var imageList = new List<IEnumerable<Point2f>>();
            foreach (double[][] view in views)
            {
                if (view.Length != obj.Length)
                    throw new ArgumentException("Every image must hold " + obj.Length + " points.");

                objectList.Add(objectPoints);
                imageList.Add(view.Select(p => new Point2f((float)p[0], (float)p[1])).ToArray());
            }

            var camera = new double[3, 3];
            var distortion = new double[5];
            Vec3d[] rvecs, tvecs;
            double rms = Cv2.CalibrateCamera(objectList, imageList, new Size(width, height), camera, distortion, out rvecs, out tvecs);

            return new CameraCalibrationResult
            {
                Rms = rms,
                Fx = camera[0, 0],
                Fy = camera[1, 1],
                Cx = camera[0, 2],
                Cy = camera[1, 2],
                Distortion = distortion.Take(5).ToArray(),
                Rvecs = rvecs.Select(v => new[] { v.Item0, v.Item1, v.Item2 }).ToArray(),
                Tvecs = tvecs.Select(v => new[] { v.Item0, v.Item1, v.Item2 }).ToArray(),
            };
        }

        /// <summary>cv2.solvePnP for the plate: its pose in one image, with K and D known.</summary>
        public static void SolvePose(CameraCalibrationResult camera, double[][] obj, double[][] img, out double[] rvec, out double[] tvec)
        {
            Point3f[] objectPoints = obj.Select(p => new Point3f((float)p[0], (float)p[1], 0f)).ToArray();
            Point2f[] imagePoints = img.Select(p => new Point2f((float)p[0], (float)p[1])).ToArray();

            using (Mat objectMat = Mat.FromArray(objectPoints))
            using (Mat imageMat = Mat.FromArray(imagePoints))
            using (Mat cameraMat = Mat.FromArray(camera.CameraMatrix))
            using (Mat distortionMat = Mat.FromArray(camera.Distortion))
            using (var rvecMat = new Mat())
            using (var tvecMat = new Mat())
            {
                Cv2.SolvePnP(objectMat, imageMat, cameraMat, distortionMat, rvecMat, tvecMat);

                rvec = new[] { rvecMat.Get<double>(0, 0), rvecMat.Get<double>(1, 0), rvecMat.Get<double>(2, 0) };
                tvec = new[] { tvecMat.Get<double>(0, 0), tvecMat.Get<double>(1, 0), tvecMat.Get<double>(2, 0) };
            }
        }
    }
}
