using NeoCortexApi;
using NeoCortexApi.Entities;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using VideoLibrary;

namespace HTMVideoLearning
{
    class Program
    {

        static void Main(string[] args)
        {
            // Reading json config file
            (VideoConfig videoConfig, HtmConfig htmConfig) = ReadConfigFile(args);

            // Reading Videos into frames
            // List<VideoSet> trainingData = getTrainingData(videoConfig);
            // TODO: adding video reading here instead of inside experiments
            // Saving all encoding of video frame may lead to memory outage
            // CONSIDER: taking only the data in VideoConfiguration then indexing the image name in Converted

            VideoLearning.TrainWithFrameKey(videoConfig, htmConfig);
        }


        #region Helper Function
        /// <summary>
        /// Configure settings for reading Videos and HtmConfig
        /// Reading Configs for Video Learning Experiment
        /// </summary>
        /// <param name="args">Console Input argument</param>
        /// <returns></returns>
        private static (VideoConfig, HtmConfig) ReadConfigFile(string[] args)
        {
            // read startup Config File
            VideoConfig videoConfig = readConfig<VideoConfig>(args[0]);

            // reading HTM Config File
            HtmConfig htmConfig = readConfig<HtmConfig>(args[1]);

            //Calculate inputbits and then turn it into an array as it is required for htm configuration
            int inputBits = videoConfig.FrameWidth * videoConfig.FrameHeight * (int)videoConfig.ColorMode;
 
            htmConfig.InputDimensions = new int[] { inputBits };
            
            ModifyHtmFromCode(ref htmConfig);
           
            return (videoConfig, htmConfig);
        }

        /// <summary>
        /// Optional solution for modifying HTM settings in code
        /// </summary>
        /// <param name="htmConfig">Htm Configuration to be modified</param>
        private static void ModifyHtmFromCode( ref HtmConfig htmConfig)
        {
            htmConfig.Random = new ThreadSafeRandom(42);

            htmConfig.CellsPerColumn = 40;
            htmConfig.GlobalInhibition = true;
            //htmConfig.LocalAreaDensity = -1;
            htmConfig.NumActiveColumnsPerInhArea = 0.02 * htmConfig.ColumnDimensions[0];
            htmConfig.PotentialRadius = (int)(0.15 * htmConfig.InputDimensions[0]);
            //htmConfig.InhibitionRadius = 15;

            htmConfig.MaxBoost = 30.0;
            htmConfig.DutyCyclePeriod = 100;
            htmConfig.MinPctOverlapDutyCycles = 0.75;
            htmConfig.MaxSynapsesPerSegment = (int)(0.02 * htmConfig.ColumnDimensions[0]);
            htmConfig.StimulusThreshold = (int)0.05 * htmConfig.ColumnDimensions[0];
            //ActivationThreshold = 15;
            //ConnectedPermanence = 0.5;

            // Learning is slower than forgetting in this case.
            //PermanenceDecrement = 0.15;
            //PermanenceIncrement = 0.15;

            // Used by punishing of segments.
        }

        /// <summary>
        /// Reading Config from jsonFile
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="inputFilePath">input json config file path</param>
        /// <returns></returns>
        private static T readConfig<T>(string inputFilePath)
        {
            string jsonString = File.ReadAllText(inputFilePath);
            T config = JsonConvert.DeserializeObject<T>(jsonString);
            return config;
        }

        /// <summary>
        /// get training dataset from VideoConfig
        /// </summary>
        /// <param name="videoConfig"></param>
        /// <returns></returns>
        /// <exception cref="NotImplementedException"></exception>
        private static List<VideoSet> getTrainingData(VideoConfig videoConfig)
        {
            throw new NotImplementedException();
        }
        #endregion
    }

}
