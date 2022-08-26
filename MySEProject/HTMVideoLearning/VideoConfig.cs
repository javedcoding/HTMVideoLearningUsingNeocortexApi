using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VideoLibrary;

namespace HTMVideoLearning
{
    public class VideoConfig
    {
        /// <summary>
        /// video frame width in pixels
        /// </summary>
        public int FrameWidth;
        /// <summary>
        /// video frame height in pixels
        /// </summary>
        public int FrameHeight;
        /// <summary>
        /// <br>video's frame rate</br>
        /// <br>must always smaller than original video</br>
        /// </summary>
        public double FrameRate;
        /// <summary>
        /// color encoding
        /// </summary>
        public ColorMode ColorMode;
        /// <summary>
        /// The root folder where training videos are stored.
        /// </summary>
        public string TrainingDatasetRoot { get; set; }

        /// <summary>
        /// test file path array after the experiment runs
        /// </summary>
        public string[] TestFiles { get; set; }
    }
}
