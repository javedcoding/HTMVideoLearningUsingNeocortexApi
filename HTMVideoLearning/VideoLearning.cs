using NeoCortexApi;
using NeoCortexApi.Classifiers;
using NeoCortexApi.Entities;
using NeoCortexApi.Network;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using VideoLibrary;

namespace HTMVideoLearning
{
    class VideoLearning
    {
        #region Run1: Learning with HtmClassifier key as FrameKey
        /// <summary>
        /// <br>Run1:</br>
        /// <br>Training and Learning Video with HTMClassifier with key as a frame key</br>
        /// <br>Testing Procedure:</br>
        /// <br>Read the Training dataset</br>
        /// <br>Preprocessing the frame into smaller resolution, lower frame rate, color to binarized value</br>
        /// <br>Learn the patterns(frames) with SP till reaching newborn stable state</br>
        /// <br>Learn the patterns(frames) with SP+TM to generate sequential relation of adjacent frames,</br>
        /// <br>     The learning ends when average accuracy is more than 90% and stays for 40 cycles or reaching maxcycles</br>
        /// <br>     Calculating Average accuracy:</br>
        /// <br>         Get the Predicted cells of the current frames SDR through TM</br>
        /// <br>         Use the Predicted cells in HTMClassifier to see if there are learned framekey</br>
        /// <br>         If the key of the next frame is found, count increase 1.</br>
        /// <br>         The average accuracy is calculated by average of videoset accuracy, </br>
        /// <br>         videoset accuracy is calculated by average of all video accuracy in that set.</br>
        /// <br>Testing session start:</br>
        /// <br>Drag an Image as input, The trained layer will try to predict the next Frame, then uses the next frame as input to continue </br>
        /// <br>as long as there are predicted cells.</br>
        /// <br>The predicted series of Frame after the input frame are made into videos under Run1Experiment/TEST/</br>
        /// </summary>
        /// <param name="videoConfig">Config values of video file</param>
        /// <param name="htmCfg">HTM config values</param>
        public static void TrainWithFrameKey(VideoConfig videoConfig = null, HtmConfig htmCfg = null)
        {
            Stopwatch sw = new();
            List<TimeSpan> RecordedTime = new();

            RenderHelloScreen();

            //string trainingFolderPath = videoConfig?.TrainingDatasetRoot ?? null;
            string trainingFolderPath = CheckIfPathExists(videoConfig);

            /*if (String.IsNullOrEmpty(trainingFolderPath))
                trainingFolderPath = Console.ReadLine();*/

            sw.Start();

            string outputFolder = nameof(VideoLearning.TrainWithFrameKey) + "_" + GetCurrentTime();
            string convertedVideoDir, testOutputFolder;

            CreateTemporaryFolders(outputFolder, out convertedVideoDir, out testOutputFolder);

            // Define Reader for Videos
            // Input videos are stored in different folders under TrainingVideos/
            // with their folder's names as label value. To get the paths of all folders:
            //string[] videoDatasetRootFolder = GetVideoSetPaths(trainingFolderPath);
            string[] videoSetDirectories = GetVideoSetPaths(trainingFolderPath);

            // A list of VideoSet object, each has the Videos and the name of the folder as Label, contains all the Data in TrainingVideos,
            // this List will be the core iterator in later learning and predicting
            List<VideoSet> videoData = new();

            // Iterate through every folder in TrainingVideos/ to create VideoSet: object that stores video of same folder/label
            foreach (string path in videoSetDirectories)
            {
                VideoSet vs = new(path, videoConfig.ColorMode, videoConfig.FrameWidth, videoConfig.FrameHeight, videoConfig.FrameRate);
                videoData.Add(vs);
                vs.ExtractFrames(convertedVideoDir);
            }
            
            var mem = new Connections(htmCfg);

            HtmClassifier<string, ComputeCycle> cls = new();

            CortexLayer<object, object> layer1 = new("L1");

            TemporalMemory tm = new();

            bool isInStableState = false;

            bool learn = true;

            //This should be 30 minimum
            int maxNumOfElementsInSequence = videoData[0].GetLongestFramesCountInSet();

            int maxCycles = 10;
            int newbornCycle = 0;

            //HomeostaticPlasticityController hpa = new(mem, maxNumOfElementsInSequence * 150 * 3, (isStable, numPatterns, actColAvg, seenInputs) =>
            HomeostaticPlasticityController hpa = new(mem, maxNumOfElementsInSequence * 150 * 3, (isStable, numPatterns, actColAvg, seenInputs) =>
            {
                if (isStable)
                    // Event should be fired when entering the stable state.
                    Debug.WriteLine($"STABLE: Patterns: {numPatterns}, Inputs: {seenInputs}, iteration: {seenInputs / numPatterns}");
                else
                    // Ideal SP should never enter unstable state after stable state.
                    Debug.WriteLine($"INSTABLE: Patterns: {numPatterns}, Inputs: {seenInputs}, iteration: {seenInputs / numPatterns}");

                // We are not learning in instable state.
                learn = isInStableState = isStable;

                // Clear all learned patterns in the classifier.
                //cls.ClearState();

            }, numOfCyclesToWaitOnChange: 50);

            SpatialPoolerMT sp = new(hpa);
            sp.Init(mem);
            tm.Init(mem);
            layer1.HtmModules.Add("sp", sp);

            //
            // Training SP to get stable. New-born stage.
            //
            ///*
            //for (int i = 0; i < maxCycles; i++)
            while (isInStableState == false)
            {
                newbornCycle++;

                Console.WriteLine($"-------------- Newborn Cycle {newbornCycle} ---------------");

                foreach (VideoSet set in videoData)
                {
                    // Show Set Label/ Folder Name of each video set
                    WriteLineColor($"VIDEO SET LABEL: {set.VideoSetLabel}", ConsoleColor.Cyan);
                    foreach (NVideo vid in set.nVideoList)
                    {
                        // Show the name of each video
                        WriteLineColor($"VIDEO NAME: {vid.name}", ConsoleColor.DarkCyan);
                        foreach (NFrame frame in vid.nFrames)
                        {
                            //Console.WriteLine($" -- {frame.FrameKey} --");
                            Console.Write(".");
                            var lyrOut = layer1.Compute(frame.EncodedBitArray, learn);

                            if (isInStableState)
                                break;
                        }
                        Console.WriteLine();
                    }
                }

                if (isInStableState)
                    break;
            }
            //*/

            layer1.HtmModules.Add("tm", tm);

            // Accuracy Check

            int cycle = 0;
            int matches = 0;

            List<string> lastPredictedValue = new();

            foreach (VideoSet vs in videoData)
            {
                // Iterating through every video in a VideoSet
                foreach (NVideo nv in vs.nVideoList)
                {
                    int maxPrevInputs = nv.nFrames.Count - 1;
                    cycle = 0;
                    learn = true;
                    sw.Reset();
                    sw.Start();

                    // Now training with SP+TM. SP is pretrained on the provided training videos.
                    // Learning each frame in a video
                    double lastCycleAccuracy = 0;
                    int saturatedAccuracyCount = 0;
                    bool isCompletedSuccessfully = false;

                    for (int i = 0; i < maxCycles; i++)
                    {
                        matches = 0;
                        cycle++;

                        Console.WriteLine($"-------------- Cycle {cycle} ---------------");

                        foreach (var currentFrame in nv.nFrames)
                        {
                            Console.WriteLine($"--------------SP+TM {currentFrame.FrameKey} ---------------");

                            // Calculating SDR from the current Frame
                            var lyrOut = layer1.Compute(currentFrame.EncodedBitArray, learn) as ComputeCycle;

                            Console.WriteLine(string.Join(',', lyrOut.ActivColumnIndicies));
                            // lyrOut is null when the TM is added to the layer inside of HPC callback by entering of the stable state

                            // In the pretrained SP with HPC, the TM will quickly learn cells for patterns
                            // In that case the starting sequence 4-5-6 might have the sam SDR as 1-2-3-4-5-6,
                            // Which will result in returning of 4-5-6 instead of 1-2-3-4-5-6.
                            // HtmClassifier allways return the first matching sequence. Because 4-5-6 will be as first
                            // memorized, it will match as the first one.

                            

                            string key = currentFrame.FrameKey;
                            List<Cell> actCells;

                            WriteLineColor($"WinnerCell Count: {lyrOut.WinnerCells.Count}", ConsoleColor.Cyan);
                            WriteLineColor($"ActiveCell Count: {lyrOut.ActiveCells.Count}", ConsoleColor.Cyan);

                            if (lyrOut.ActiveCells.Count == lyrOut.WinnerCells.Count)
                            {
                                actCells = lyrOut.ActiveCells;
                            }
                            else
                            {
                                actCells = lyrOut.WinnerCells;
                            }

                            //This is where two training functions defers, here FrameKey is used for HTMClassifier learning
                            // Remember the key with corresponding SDR using HTMClassifier to assign the current frame key with the Collumns Indicies array
                            WriteLineColor($"Current learning Key: {key}", ConsoleColor.Magenta);
                            cls.Learn(currentFrame.FrameKey, actCells.ToArray());

                            if (learn == false)
                                Console.WriteLine("Inference mode");

                            Console.WriteLine($"Col  SDR: {Helpers.StringifyVector(lyrOut.ActivColumnIndicies)}");
                            Console.WriteLine($"Cell SDR: {Helpers.StringifyVector(actCells.Select(c => c.Index).ToArray())}");

                            if (lastPredictedValue.Contains(key))
                            {
                                matches++;
                                Console.WriteLine($"Match. Actual value: {key} - Predicted value: {key}");
                                lastPredictedValue.Clear();
                            }
                            else
                            {
                                Console.WriteLine($"Mismatch! Actual value: {key} - Predicted values: {String.Join(',', lastPredictedValue)}");
                                lastPredictedValue.Clear();
                            }

                            // Checking Predicted Cells of the current frame
                            // From experiment the number of Predicted cells increase over cycles and reach stability later.
                            if (lyrOut.PredictiveCells.Count > 0)
                            {
                                var predictedInputValues = cls.GetPredictedInputValues(lyrOut.PredictiveCells.ToArray(), 1);

                                foreach (var item in predictedInputValues)
                                {
                                    Console.WriteLine($"Current Input: {currentFrame.FrameKey} \t| Predicted Input: {item.PredictedInput}");
                                    lastPredictedValue.Add(item.PredictedInput);
                                }
                            }
                            else
                            {
                                Console.WriteLine("NO CELLS PREDICTED for next cycle.");
                                lastPredictedValue.Clear();
                            }
                        }
                        double accuracy;

                        accuracy = (double)matches / ((double)nv.nFrames.Count - 1.0) * 100.0;
                        UpdateAccuracy(vs.VideoSetLabel, nv.name, accuracy, Path.Combine(outputFolder, "TEST"));

                        Console.WriteLine($"Cycle: {cycle}\tMatches={matches} of {nv.nFrames.Count}\t {accuracy}%");
                        if (accuracy == lastCycleAccuracy)
                        {
                            // The learning may result in saturated accuracy
                            // Unable to learn to higher accuracy, Exit
                            saturatedAccuracyCount += 1;
                            if (saturatedAccuracyCount >= 24 && lastCycleAccuracy >= 85)
                            {
                                List<string> outputLog = new();
                                MakeDirectoryIfRequired(Path.Combine(outputFolder, "TEST"));

                                string fileName = Path.Combine(outputFolder, "TEST", $"saturatedAccuracyLog_{nv.label}_{nv.name}");
                                outputLog.Add($"Result Log for reaching saturated accuracy at {accuracy}");
                                outputLog.Add($"Label: {nv.label}");
                                outputLog.Add($"Video Name: {nv.name}");
                                outputLog.Add($"Stop after {cycle} cycles");
                                outputLog.Add($"Elapsed time: {sw.ElapsedMilliseconds / 1000 / 60} min.");
                                outputLog.Add($"reaching stable after enter newborn cycle {newbornCycle}.");
                                RecordResult(outputLog, fileName);

                                isCompletedSuccessfully = true;

                                break;
                            }
                        }
                        else
                        {
                            saturatedAccuracyCount = 0;
                        }
                        lastCycleAccuracy = accuracy;

                        // Reset Temporal memory after learning 1 time the video/sequence
                        tm.Reset(mem);
                    }
                    if (isCompletedSuccessfully == false)
                    {
                        Console.WriteLine($"The experiment didn't complete successully. Exit after {maxCycles}!");

                    }
                    Console.WriteLine("------------ END ------------");
                }
            }
            //Testing Section
            string userInput;

            MakeDirectoryIfRequired(testOutputFolder);

            int testNo = 0;

            // Test from startupConfig.json
            foreach (var testFilePath in videoConfig.TestFiles)
            {
                testNo = PredictImageInput(videoData, cls, layer1, testFilePath, testOutputFolder, testNo);
            }


            // Manual input from user
            WriteLineColor("Drag an image as input to recall the learned Video or type (Write Q to quit): ");

            userInput = Console.ReadLine().Replace("\"", "");

            while (userInput != "Q")
            {
                testNo = PredictImageInput(videoData, cls, layer1, userInput, testOutputFolder, testNo);
                WriteLineColor("Drag an image as input to recall the learned Video or type (Write Q to quit): ");
                userInput = Console.ReadLine().Replace("\"", "");
            }
        }
        #endregion

        #region Predict test for Tranined Data
        /// <summary>
        /// Predict series from input Image.
        /// <br>Process:</br>
        /// <br>Binarize input image</br>
        /// <br>Convert the binarized image to SDR via Spatial Pooler</br>
        /// <br>Get Predicted Cells from Compute output </br>
        /// <br>Compare the predicted Cells with learned HTMClassifier</br>
        /// <br>Create predicted image sequence as Video from classifier output and video database videoData </br>
        /// </summary>
        /// <param name="frameWidth">image framewidth</param>
        /// <param name="frameHeight"></param>
        /// <param name="colorMode"></param>
        /// <param name="videoData"></param>
        /// <param name="cls"></param>
        /// <param name="layer1"></param>
        /// <param name="userInput"></param>
        /// <param name="testOutputFolder"></param>
        /// <param name="testNo"></param>
        /// <returns></returns>
        private static int PredictImageInput(List<VideoSet> videoData, HtmClassifier<string, ComputeCycle> cls, CortexLayer<object, object> layer1, string userInput, string testOutputFolder, int testNo)
        {
            //Question Arise if it is used in program.cs then a normal user can not say which layer it belongs to and currently it's hard coded
            // TODO: refactor video library for easier access to these properties
            (int frameWidth, int frameHeight, ColorMode colorMode) = videoData[0].VideoSetConfig();

            string Outputdir = Path.Combine(testOutputFolder, $"Predicted from {Path.GetFileNameWithoutExtension(userInput)}");
            MakeDirectoryIfRequired(Outputdir);
            testNo += 1;
            // Save the input Frame as NFrame
            NFrame inputFrame = new(new Bitmap(userInput), "TEST", "test", 0, frameWidth, frameHeight, colorMode);
            inputFrame.SaveFrame(Path.Combine(Outputdir, $"Converted_{Path.GetFileName(userInput)}"));
            // Compute the SDR of the Frame
            var lyrOut = layer1.Compute(inputFrame.EncodedBitArray, false) as ComputeCycle;

            // Use HTMClassifier to calculate 5 possible next Cells Arrays
            var predictedInputValue = cls.GetPredictedInputValues(lyrOut.PredictiveCells.ToArray(), 5);

            WriteLineColor("Predicting for " + Path.GetFileNameWithoutExtension(userInput), ConsoleColor.Red);

            foreach (var serie in predictedInputValue)
            {
                WriteLineColor("Predicted Series:", ConsoleColor.Green);
                //Here the frame matching accuracy is calculated
                double objectAccuracy = serie.Similarity;
                string s = serie.PredictedInput;
                //Create List of NFrame to write to Video
                List<NFrame> outputNFrameList = new();
                string Label = "";
                List<string> frameKeyList = s.Split("-").ToList();
                string[] frameName = frameKeyList[0].Split('_');
                WriteLineColor($"{objectAccuracy}% match found with " + frameName[0]);
                string details = $"{objectAccuracy}% match found with " + frameName[0] + "\n" + s;
                UpdateAccuracy(Path.GetFileNameWithoutExtension(userInput), Path.GetFileNameWithoutExtension(userInput), objectAccuracy, Outputdir, details);
                Console.WriteLine("\n");
                foreach (string frameKey in frameKeyList)
                {
                    foreach (var vs in videoData)
                    {
                        foreach (var vd in vs.nVideoList)
                        {
                            foreach (var nf in vd.nFrames)
                            {
                                if (nf.FrameKey == frameKey)
                                {
                                    Label = nf.label;
                                    outputNFrameList.Add(nf);
                                    break;
                                }
                            }
                        }
                    }
                }

                // Create output video
                NVideo.CreateVideoFromFrames(
                    outputNFrameList,
                    Path.Combine(Outputdir, $"testNo_{testNo}_Label{Label}_similarity{serie.Similarity}_No of same bit{serie.NumOfSameBits}"),
                    (int)videoData[0].nVideoList[0].frameRate,
                    new Size((int)videoData[0].nVideoList[0].frameWidth, (int)videoData[0].nVideoList[0].frameHeight),
                    true);
            }

            return testNo;
        }
        #endregion

        #region Run2: Learning with HTMClassifier key as Series of FrameKey (Sequence Learning)
        /// <summary>
        /// <br> Run2:</br>
        /// <br> Training and Learning Video with HTMClassifier with key as a serie of framekey</br>
        /// <br> Testing Procedure:</br>
        /// <br> Read the Training dataset</br>
        /// <br> Preprocessing the frame into smaller resolution, lower frame rate, color to binarized value</br>
        /// <br> Learn the patterns(frames) with SP till reaching newborn stable state</br>
        /// <br> Learn the patterns(serie of frames) with SP+TM,</br>
        /// <br> The serie of frames add each framekey respectively untill it reached the videos' framecount lengths:30</br>
        /// <br> Then key - serie of frames with current frame as last frame is learned with the Cells index of the current frame.</br>
        /// <br>      e.g. current frame circle_vd1_3's cell will be associate with key "circle_vd1_4-circle_vd1_5-circle_vd1_6-...-circle_vd1_29-circle_vd1_0-circle_vd1_1-circle_vd1_2-circle_vd1_3"</br>
        /// <br>      through each iteration of frames in a video, the key will be framekey-shifted</br>
        /// <br>      a List of Last Predicted Values is saved every frame iteration to be used in the next as validation.</br>
        /// <br>          if LastPredictedValue of previous Frame contains the current frame's key, then match increase 1</br>
        /// <br>          Accuracy is calculated each iteration of each Videos.</br>
        /// <br>          The training ends when accuracy surpasses 80% more than 30 times or reaching max cycle</br>
        /// <br> Testing session start:</br>
        /// <br> Drag an Image as input, The trained layer will try to predict the next Frame, then uses the next frame label - framekey series</br>
        /// <br> to recreate the video under Run2Experiment/TEST/</br>
        /// </summary>
        /// <param name="videoConfig">Config values of video file</param>
        /// <param name="htmCfg">HTM config values</param>
        public static void TrainWithFrameKeys(VideoConfig videoConfig = null, HtmConfig htmCfg = null)
        {
            RenderHelloScreen();
        
            //initiate time capture
            Stopwatch sw = new();
            List<TimeSpan> RecordedTime = new();
            string trainingFolderPath = CheckIfPathExists(videoConfig);

            // Starting experiment
            sw.Start();

            // Output folder initiation
            string outputFolder = nameof(VideoLearning.TrainWithFrameKeys) + "_" + GetCurrentTime();

            string convertedVideoDir, testOutputFolder;

            CreateTemporaryFolders(outputFolder, out convertedVideoDir, out testOutputFolder);

            // Video Parameter 
            //Initiate configuration

            // Define Reader for Videos
            // Input videos are stored in different folders under TrainingVideos/
            // with their folder's names as label value. To get the paths of all folders:
            string[] videoSetDirectories = GetVideoSetPaths(trainingFolderPath);

            // A list of VideoSet object, each has the Videos and the name of the folder as Label, contains all the Data in TrainingVideos,
            // this List will be the core iterator in later learning and predicting
            List<VideoSet> videoData = new();

            // Iterate through every folder in TrainingVideos/ to create VideoSet: object that stores video of same folder/label
            foreach (string path in videoSetDirectories)
            {
                VideoSet vs = new(path, videoConfig.ColorMode, videoConfig.FrameWidth, videoConfig.FrameHeight, videoConfig.FrameRate);
                videoData.Add(vs);
                // Output converted Videos to Output/Converted/
                vs.ExtractFrames(convertedVideoDir);
            }

            // Define HTM parameters

            //Initiating HTM

            var mem = new Connections(htmCfg);

            HtmClassifier<string, ComputeCycle> cls = new();

            CortexLayer<object, object> layer1 = new("L1");

            TemporalMemory tm = new();

            bool isInStableState = false;

            bool learn = true;

            int maxCycles = 1000;
            int newbornCycle = 0;
            int maxNumOfElementsInSequence = videoData[0].GetLongestFramesCountInSet();
            //hpa should hold maxelement in sequence
            HomeostaticPlasticityController hpa = new(mem, maxNumOfElementsInSequence * 150 * 3, (isStable, numPatterns, actColAvg, seenInputs) =>
            {
                if (isStable)
                    // Event should be fired when entering the stable state.
                    Console.WriteLine($"STABLE: Patterns: {numPatterns}, Inputs: {seenInputs}, iteration: {seenInputs / numPatterns}");
                else
                    // Ideal SP should never enter unstable state after stable state.
                    Console.WriteLine($"INSTABLE: Patterns: {numPatterns}, Inputs: {seenInputs}, iteration: {seenInputs / numPatterns}");

                // We are not learning in instable state.
                learn = isInStableState = isStable;

                // Clear all learned patterns in the classifier.
                //cls.ClearState();

            }, numOfCyclesToWaitOnChange: 50);

            SpatialPoolerMT sp = new(hpa);
            sp.Init(mem);
            tm.Init(mem);
            layer1.HtmModules.Add("sp", sp);

            //
            // Training SP to get stable. New-born stage.
            //
            ///*
            ///normally change it to while, only for less working time use the for loop
            //for (int i = 0; i < maxCycles; i++)
            while (isInStableState == false)
            {
                newbornCycle++;
                Console.WriteLine($"-------------- Newborn Cycle {newbornCycle} ---------------");
                foreach (VideoSet set in videoData)
                {
                    // Show Set Label/ Folder Name of each video set
                    WriteLineColor($"VIDEO SET LABEL: {set.VideoSetLabel}", ConsoleColor.Cyan);
                    foreach (NVideo vid in set.nVideoList)
                    {
                        // Name of the Video That is being trained 
                        WriteLineColor($"VIDEO NAME: {vid.name}", ConsoleColor.DarkCyan);
                        foreach (NFrame frame in vid.nFrames)
                        {
                            Console.Write(".");
                            var lyrOut = layer1.Compute(frame.EncodedBitArray, learn);
                            if (isInStableState)
                                break;
                        }
                        Console.WriteLine();
                    }
                }

                if (isInStableState)
                    break;
            }
            //*/

            layer1.HtmModules.Add("tm", tm);
            List<int[]> stableAreas = new();

            int cycle = 0;
            int matches = 0;

            List<string> lastPredictedValue = new();

            foreach (VideoSet vd in videoData)
            {
                foreach (NVideo nv in vd.nVideoList)
                {
                    int maxPrevInputs = nv.nFrames.Count - 1;
                    List<string> previousInputs = new();
                    cycle = 0;
                    learn = true;

                    sw.Reset();
                    sw.Start();
                    /*int maxMatchCnt = 0;*/
                    //
                    // Now training with SP+TM. SP is pretrained on the given VideoSet.
                    // There is a little difference between a input pattern set and an input video set,
                    // The reason is because a video consists of continously altering frame, not distinct values like the sequence learning of Scalar value.
                    // Thus Learning with sp alone was kept
                    double lastCycleAccuracy = 0;
                    int saturatedAccuracyCount = 0;
                    bool isCompletedSuccessfully = false;

                    for (int i = 0; i < maxCycles; i++)
                    {
                        matches = 0;
                        cycle++;

                        Console.WriteLine($"-------------- Cycle {cycle} ---------------");

                        foreach (var currentFrame in nv.nFrames)
                        {
                            Console.WriteLine($"-------------- {currentFrame.FrameKey} ---------------");
                            var lyrOut = layer1.Compute(currentFrame.EncodedBitArray, learn) as ComputeCycle;

                            Console.WriteLine(string.Join(',', lyrOut.ActivColumnIndicies));
                            // lyrOut is null when the TM is added to the layer inside of HPC callback by entering of the stable state.

                            previousInputs.Add(currentFrame.FrameKey);
                            if (previousInputs.Count > (maxPrevInputs + 1))
                                previousInputs.RemoveAt(0);

                            // In the pretrained SP with HPC, the TM will quickly learn cells for patterns
                            // In that case the starting sequence 4-5-6 might have the sam SDR as 1-2-3-4-5-6,
                            // Which will result in returning of 4-5-6 instead of 1-2-3-4-5-6.
                            // HtmClassifier allways return the first matching sequence. Because 4-5-6 will be as first
                            // memorized, it will match as the first one.
                            if (previousInputs.Count < maxPrevInputs)
                                continue;

                            string key = GetKey(previousInputs);
                            List<Cell> actCells;

                            WriteLineColor($"WinnerCell Count: {lyrOut.WinnerCells.Count}", ConsoleColor.Cyan);
                            WriteLineColor($"ActiveCell Count: {lyrOut.ActiveCells.Count}", ConsoleColor.Cyan);

                            if (lyrOut.ActiveCells.Count == lyrOut.WinnerCells.Count)
                            {
                                actCells = lyrOut.ActiveCells;
                            }
                            else
                            {
                                actCells = lyrOut.WinnerCells;
                            }

                            //This is where two training functions defers, here a series of FrameKeys are used for HTMClassifier learning
                            // Remember the key with corresponding SDR
                            WriteLineColor($"Current learning Key: {key}", ConsoleColor.Magenta);
                            cls.Learn(key, actCells.ToArray());

                            if (learn == false)
                                Console.WriteLine("Inference mode");

                            Console.WriteLine($"Col  SDR: {Helpers.StringifyVector(lyrOut.ActivColumnIndicies)}");
                            Console.WriteLine($"Cell SDR: {Helpers.StringifyVector(actCells.Select(c => c.Index).ToArray())}");

                            if (lastPredictedValue.Contains(key))
                            {
                                matches++;
                                Console.WriteLine($"Match. Actual value: {key} - Predicted value: {key}");
                                lastPredictedValue.Clear();
                            }
                            else
                            {
                                Console.WriteLine($"Mismatch! Actual value: {key} - Predicted values: {String.Join(',', lastPredictedValue)}");
                                lastPredictedValue.Clear();
                            }

                            if (lyrOut.PredictiveCells.Count > 0)
                            {
                                //var predictedInputValue = cls.GetPredictedInputValue(lyrOut.PredictiveCells.ToArray());
                                var predictedInputValues = cls.GetPredictedInputValues(lyrOut.PredictiveCells.ToArray(), 1);

                                foreach (var item in predictedInputValues)
                                {
                                    Console.WriteLine($"Current Input: {currentFrame.FrameKey} \t| Predicted Input: {item.PredictedInput}");
                                    lastPredictedValue.Add(item.PredictedInput);
                                }
                            }
                            else
                            {
                                Console.WriteLine("NO CELLS PREDICTED for next cycle.");
                                lastPredictedValue.Clear();
                            }
                        }

                        double accuracy;

                        accuracy = (double)matches / ((double)nv.nFrames.Count - 1.0) * 100.0; // Use if with reset
                        //accuracy = (double)matches / (double)nv.nFrames.Count * 100.0; // Use if without reset
                        UpdateAccuracy(vd.VideoSetLabel, nv.name, accuracy, Path.Combine(outputFolder, "TEST"));

                        Console.WriteLine($"Cycle: {cycle}\tMatches={matches} of {nv.nFrames.Count}\t {accuracy}%");
                        if (accuracy == lastCycleAccuracy)
                        {
                            // The learning may result in saturated accuracy
                            // Unable to learn to higher accuracy, Exit
                            saturatedAccuracyCount += 1;
                            if (saturatedAccuracyCount >= 24 && lastCycleAccuracy >= 85)
                            {
                                List<string> outputLog = new();
                                MakeDirectoryIfRequired(Path.Combine(outputFolder, "TEST"));
                                string fileName = Path.Combine(outputFolder, "TEST", $"saturatedAccuracyLog_{nv.label}_{nv.name}");
                                outputLog.Add($"Result Log for reaching saturated accuracy at {accuracy}");
                                outputLog.Add($"Label: {nv.label}");
                                outputLog.Add($"Video Name: {nv.name}");
                                outputLog.Add($"Stop after {cycle} cycles");
                                outputLog.Add($"Elapsed time: {sw.ElapsedMilliseconds / 1000 / 60} min.");
                                outputLog.Add($"reaching stable after enter newborn cycle {newbornCycle}.");
                                RecordResult(outputLog, fileName);

                                isCompletedSuccessfully = true;

                                break;
                            }
                        }
                        else
                        {
                            saturatedAccuracyCount = 0;
                        }
                        lastCycleAccuracy = accuracy;
                        //learn = true;

                        // Reset Temporal memory after learning 1 time the video/sequence
                        tm.Reset(mem);
                    }

                    if (isCompletedSuccessfully == false)
                    {
                        Console.WriteLine($"The experiment didn't complete successully. Exit after {maxCycles}!");

                    }
                    Console.WriteLine("------------ END ------------");
                    previousInputs.Clear();
                }
            }
            //Testing Section
            string userInput;

            MakeDirectoryIfRequired(testOutputFolder);

            int testNo = 0;

            // Test from startupConfig.json
            foreach (var testFilePath in videoConfig.TestFiles)
            {
                testNo = PredictImageInput(videoData, cls, layer1, testFilePath, testOutputFolder, testNo);
            }


            // Manual input from user
            WriteLineColor("Drag an image as input to recall the learned Video or type (Write Q to quit): ");

            userInput = Console.ReadLine().Replace("\"", "");

            while (userInput != "Q")
            {
                testNo = PredictImageInput(videoData, cls, layer1, userInput, testOutputFolder, testNo);
                WriteLineColor("Drag an image as input to recall the learned Video or type (Write Q to quit): ");
                userInput = Console.ReadLine().Replace("\"", "");
            }
        }

        /// <summary>
        /// Current timestamp to know when the program was started 
        /// </summary>
        /// <returns>Current time without any slash</returns>
        private static string GetCurrentTime()
        {
            var currentTime = DateTime.Now.ToString();

            var timeWithoutSpace = currentTime.Split();

            var timeWithUnderScore = string.Join("_", timeWithoutSpace);

            var timeWithoutColon = timeWithUnderScore.Replace(':', '-');

            var timeWithoutSlash = timeWithoutColon.Replace('/', '-');

            return timeWithoutSlash;
        }

        /// <summary>
        /// Checking if Training DatasetRoot is defined in videoConfig.json
        /// if not Prompt the user to manually input the path to the program 
        /// </summary>
        /// <param name="videoConfig">Config values of video file</param>
        /// <returns>Training folder path</returns>
        private static string CheckIfPathExists(VideoConfig videoConfig)
        {
            string trainingFolderPath = videoConfig?.TrainingDatasetRoot ?? null;

            if (String.IsNullOrEmpty(trainingFolderPath))
            {
                WriteLineColor("training Dataset path not detectected in startupConfig.json", ConsoleColor.Blue);
                WriteLineColor("Please drag the folder that contains the training files to the Console Window: ", ConsoleColor.Blue);
                WriteLineColor("For example sample set SmallTrainingSet/ is located in root directory", ConsoleColor.Blue);
                trainingFolderPath = Console.ReadLine();
            }

            return trainingFolderPath;
        }

        #endregion

        #region Key generation for HtmClassifier
        /// <summary>
        /// Get the key for HTMClassifier learning stage.
        /// The key here is a serie of frames' keys, seperated by "-"
        /// </summary>
        /// <param name="prevInputs"></param>
        /// <returns></returns>
        private static string GetKey(List<string> prevInputs)
        {
            string key = string.Join("-", prevInputs);
            return key;
        }
        #endregion

        #region private help methods
        /// <summary>
        /// Write accuracy of the cycle into result files 
        /// </summary>
        /// <param name="labelName">Name of the Label</param>
        /// <param name="videoName">Name of the video</param>
        /// <param name="accuracy">accuracy value</param>
        /// <param name="outputFolder"> Path of the directory where the output will be saved</param>
        private static void UpdateAccuracy(string labelName, string videoName, double accuracy, string outputFolder, string extra = null)
        {
            string fileName = $"{videoName}_accuracy.txt";
            string path = Path.Combine(outputFolder,"AccuracyLog",labelName);

            

            MakeDirectoryIfRequired(path);

            string fullPath = Path.Combine(path,fileName);
            using (StreamWriter sw = File.AppendText(fullPath))
            {
                if(extra != null)
                {
                    sw.WriteLine(extra);
                }
                sw.WriteLine(accuracy);
            }
        }
        /// <summary>
        /// Writing experiment result to write to a text file
        /// </summary>
        /// <param name="possibleOutcomeSerie"></param>
        /// <param name="inputVideo"></param>
        private static void RecordResult(List<string> result, string fileName)
        {
            File.WriteAllLines($"{fileName}.txt", result);
        }

        // Hello screen
        // TODO: adding instruction/ introduction/ experiment flow
        private static void RenderHelloScreen()
        {
            WriteLineColor($"Hello NeoCortexApi! Conducting experiment {nameof(VideoLearning)} CodeBreakers" + "\n" + 
                "This program can take initial information of the training video from VideoConfig.json" + "\n" +
                "If you are training with a new set of videos please place the videos in the folder name SmallTrainingSet" + "\n" +
                "Moreover you also need to give video metadata information in the VideoConfig.json file" + "\n" +
                "To change HTMClassifier configuration use htmConfig.json");
        }

        /// <summary>
        /// Create folders required for the experiment.
        /// </summary>
        /// <param name="outputFolder">Output folder</param>
        /// <param name="convertedVideoDir">Converted Video directory</param>
        /// <param name="testOutputFolder">Test output folder</param>
        private static void CreateTemporaryFolders(string outputFolder, out string convertedVideoDir, out string testOutputFolder)
        {
            MakeDirectoryIfRequired(outputFolder);

            convertedVideoDir = Path.Combine(outputFolder,"Converted");
            MakeDirectoryIfRequired(convertedVideoDir);

            testOutputFolder = Path.Combine(outputFolder,"TEST");
            MakeDirectoryIfRequired(testOutputFolder);
        }
        /// <summary>
        /// Print a line in Console with color and/or hightlight
        /// <param name="str">string to print</param>
        /// <param name="foregroundColor">Text color</param>
        /// <param name="backgroundColor">Hightlight Color</param>
        /// </summary>
        public static void WriteLineColor(
            string str,
            ConsoleColor foregroundColor = ConsoleColor.White,
            ConsoleColor backgroundColor = ConsoleColor.Black)
        {
            Console.ForegroundColor = foregroundColor;
            Console.BackgroundColor = backgroundColor;
            Console.WriteLine(str);
            Console.ResetColor();
        }
        /// <summary>
        /// Gets directories inside the passed parent directory
        /// <param name="folderPath">Absolute path of the parent folder</param>
        /// <returns>Returns an array of each video set's directory</returns>
        /// </summary>
        public static string[] GetVideoSetPaths(string folderPath)
        {
            // remove the two outer quotation marks
            folderPath = folderPath.Replace("\"", "");
            string[] videoSetPaths = Array.Empty<string>();
            string testDir;
            if (Directory.Exists(folderPath))
            {
                testDir = folderPath;
                WriteLineColor("Inserted Path is found", ConsoleColor.Green);
                Console.WriteLine($"Begin reading directory: {folderPath} ...");
            }
            else
            {
                string currentDir = Directory.GetCurrentDirectory();
                WriteLineColor("The inserted path for the training folder is invalid. " +
                    $"If you have trouble adding the path, copy your training folder with name TrainingVideos to {currentDir}", ConsoleColor.Yellow);
                // Get the root path of training videos
                testDir = Path.Combine(currentDir, "TrainingVideos");
            }
            // Get all the folders that contain video sets under TrainingVideos
            try
            {
                videoSetPaths = Directory.GetDirectories(testDir, "*", SearchOption.TopDirectoryOnly);
                WriteLineColor("Complete reading directory ...");
                return videoSetPaths;
            }
            catch (Exception e)
            {
                WriteLineColor("=========== Caught exception ============", ConsoleColor.Magenta);
                WriteLineColor(e.Message, ConsoleColor.Magenta);
                return videoSetPaths;
            }
        }
        /// <summary>
        /// If the directory does not exist, it enters the directory
        /// <param name="path">directory path</param>
        private static void MakeDirectoryIfRequired (string path)
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
        }
        #endregion
    }
}
