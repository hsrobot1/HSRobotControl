using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using System.Diagnostics;
using System.IO.Ports;
using Manager;
using IllusionUtility.GetUtility;
using System.Configuration;
using System.Reflection;

namespace HSRobotControl
{
    public class HSRobotControlPlugin : IllusionPlugin.IPlugin
    {
        // Gets the name of the plugin.
        public string Name { get; } = "HSRobotControl";

        /// Gets the version of the plugin.
        public string Version { get; } = "1.0";

        // Stopwatch for ms timing
        private Stopwatch sw = Stopwatch.StartNew();

        // Variables for chara indexing
        private int femaleCount = 0;
        private int femaleIndex = 0;
        private int maleCount = 0;
        private int maleIndex = 0;

        // Configuration variables below from HSRobotControl.dll.config
        private SerialPort serialPort;
        private int serialPortBaudRate;
        private float robotUpdateFrequency;
        private bool autoRange;
        private float autoRangeTime;
        private bool charaDiagnostics;
        private bool configDiagnostics;

        // Variables for female chara targeting
        private string[] targetNames;
        private string[] targetBoneNames;
        // targetPriorities schema: "<Closest target found>|<Prioritize as closest target instead>|<Prioritize as closest target instead>|..."
        // So if the leftmost target name string value is the closest female chara target (bone) to the male chara penis (bone)
        // then prioritize any successive target name in the string to the right of the first '|' and chose it over the actual closest female chara target (bone)
        private string[] targetPriorities;
        private float[] targetPriorityRange;
        private float[] targetRangeMin;
        private float[] targetRangeMax;
        private float[] targetAutoRangeValues;
        private float targetPriorityAutoRangeTolerance;
        private Vector3[] targetPositions;
        private float[] targetDistances;
        private float targetDistanceRangeThreshold;

        // Updates the robot's position based on the distance from the closest female chara's targets (bones) to the chara male's penis (bone)
        // If a female chara target (bone) priority exists and in the target range then it is used instead of the closest target (bone)
        private void UpdateRobotPosition()
        {
            // Find all the female and male chara in the current scene
            Character charaManager = Character.Instance;
            var females = charaManager.dictFemale.Values.Where(x => x.animBody != null).Select(x => x as CharInfo);
            var males = charaManager.dictMale.Values.Where(x => x.animBody != null).Select(x => x as CharInfo);

            // Record the female and male chara count
            femaleCount = females.Count();
            maleCount = males.Count();

            //if (charaDiagnostics)
                //Console.WriteLine("Females: {0}, Males: {1}", femaleCount, maleCount);

            // Used for index tracking in the foreach loops
            int index = 0;

            // Iterate through the female chara and record the target (bone) positions of only the desired female chara by index.
            // The index value is changed by pressing the C button on the keyboard and only changes if there is more than
            // one female chara in the current scene
            foreach (var chara in females)
            {
                if (index == femaleIndex)
                {
                    for (int i = 0; i < targetNames.Length; i++)
                    {
                        string[] bones = targetBoneNames[i].Split('|');

                        Vector3 bonePosition;
                        bonePosition.x = 0.0f;
                        bonePosition.y = 0.0f;
                        bonePosition.z = 0.0f;

                        for (int b = 0; b < bones.Length; b++)
                        {
                            bonePosition += chara.chaBody.objBone.transform.FindLoop(bones[b]).transform.position;
                        }

                        targetPositions[i] = bonePosition / (float)bones.Length;

                        if (charaDiagnostics)
                            Console.WriteLine("Female chara ({0})'s {1} is at {2}, {3}, {4}", index, targetNames[i], targetPositions[i].x, targetPositions[i].y, targetPositions[i].z);
                    }
                }

                index++;
            }

            // Male chara's penis target (bone) position
            Vector3 penis;
            penis.x = 0.0f;
            penis.y = 0.0f;
            penis.z = 0.0f;

            index = 0;

            // Iterate through the male chara and record the penis target (bone) position of only the desired male chara by index.
            // The index value is changed by pressing the Shift+C button on the keyboard and only changes if there is more than
            // one male chara in the current scene
            foreach (var chara in males)
            {
                if (index == maleIndex)
                {
                    penis = chara.chaBody.objBone.transform.FindLoop("cm_J_dan_s").transform.position;

                    if (charaDiagnostics)
                        Console.WriteLine("Male chara ({0})'s penis at {1}, {2}, {3}", index, penis.x, penis.y, penis.z);
                }

                index++;
            }

            // If there is at least one female and male chara in the current scene then find the
            // nearest female chara's target (bone) to the male chara's penis target (bone)
            if (femaleCount > 0 && maleCount > 0)
            {
                float minDistance = 999999999.0f;
                int minIndex = 0;

                for (int i = 0; i < targetNames.Length; i++)
                {
                    targetDistances[i] = Vector3.Distance(targetPositions[i], penis);

                    if (charaDiagnostics)
                        Console.WriteLine("Distance from Female chara ({0})'s {1} to Male chara ({2})'s penis is {3}", femaleIndex, targetNames[i], maleIndex, targetDistances[i]);

                    if (targetDistances[i] < minDistance)
                    {
                        minDistance = targetDistances[i];
                        minIndex = i;
                    }
                }

                float targetAutoRangeValuesMin = 0.0f;
                float targetAutoRangeValuesMax = 0.0f;

                // Find and assign min and max values in targetAutoRangeValues array
                if (autoRange)
                {
                    targetAutoRangeValuesMin = targetAutoRangeValues.Min();
                    targetAutoRangeValuesMax = targetAutoRangeValues.Max();
                }

                if (charaDiagnostics)
                    Console.WriteLine("Female chara ({0})'s {1} is the closest to Male chara ({2})", femaleIndex, targetNames[minIndex], maleIndex);

                // Priority used flag
                bool pFlag = false;

                // If the closest female chara's target (bone) has priority targets and they are in the priority target (bone) distance range 
                // to the male chara's penis target (bone) then use the female chara's priority target (bone) instead
                // targetPriorities schema: "<Closest target found>|<Prioritize as closest target instead>|<Prioritize as closest target instead>|..."
                for (int i = 0; i < targetPriorities.Length; i++)
                {
                    // Split target (bone) priorities string
                    string[] priorities = targetPriorities[i].Split('|');

                    // Check if the current closest female chara's target (bone) has target (bone) priorities
                    if (minIndex == Array.IndexOf(targetNames, priorities[0]))
                    {
                        float minDistancePriority = 999999999.0f;

                        // Check if any of the target (bone) priorities are in their acceptable distance ranges and if so select the closest
                        for (int p = 1; p < priorities.Length; p++)
                        {
                            int pIndex = Array.IndexOf(targetNames, priorities[p]);

                            float priorityRangeMin;
                            float priorityRangeMax;

                            if (autoRange)
                            {
                                priorityRangeMin = targetAutoRangeValuesMin - targetPriorityAutoRangeTolerance;
                                priorityRangeMax = targetAutoRangeValuesMax + targetPriorityAutoRangeTolerance;
                            }
                            else
                            {
                                priorityRangeMin = targetPriorityRange[0];
                                priorityRangeMax = targetPriorityRange[1];
                            }

                            if (targetDistances[pIndex] >= priorityRangeMin && targetDistances[pIndex] <= priorityRangeMax)
                            {
                                if (targetDistances[pIndex] < minDistancePriority)
                                {
                                    minDistancePriority = targetDistances[pIndex];
                                    minIndex = pIndex;
                                    pFlag = true;
                                }
                            }
                        }
                    }
                }

                // If a female chara's priority target (bone) was found
                if (pFlag)
                {
                    if (charaDiagnostics)
                        Console.WriteLine("Female chara ({0})'s {1} takes priority as the closest to Male chara ({2})", femaleIndex, targetNames[minIndex], maleIndex);
                }

                // Shift targetAutoRangeValues array and append the closest new target (bone) distance
                if (autoRange)
                {
                    Array.Copy(targetAutoRangeValues, 1, targetAutoRangeValues, 0, targetAutoRangeValues.Length - 1);

                    targetAutoRangeValues[targetAutoRangeValues.Length - 1] = targetDistances[minIndex];

                    targetAutoRangeValuesMin = targetAutoRangeValues.Min();
                    targetAutoRangeValuesMax = targetAutoRangeValues.Max();
                }

                float distanceRangeMin;
                float distanceRangeMax;

                if (autoRange)
                {
                    distanceRangeMin = targetAutoRangeValuesMin;
                    distanceRangeMax = targetAutoRangeValuesMax;
                }
                else
                {
                    distanceRangeMin = targetRangeMin[minIndex];
                    distanceRangeMax = targetRangeMax[minIndex];
                }

                if (charaDiagnostics)
                    Console.WriteLine("Distance Range: {0} to {1}", distanceRangeMin, distanceRangeMax);

                // If the female chara's target (bone) is in it's distance range to the male chara's penis target (bone)
                if (targetDistances[minIndex] >= distanceRangeMin && targetDistances[minIndex] <= distanceRangeMax && (distanceRangeMax - distanceRangeMin) >= targetDistanceRangeThreshold)
                {
                    if (charaDiagnostics)
                        Console.WriteLine("Female chara ({0}) is using her {1} on Male chara ({2})", femaleIndex, targetNames[minIndex], maleIndex);

                    try
                    {
                        // Serial port robot command schema: "<distance from female target to male's penis> <female target's distance range min> <female target's distance range max>"
                        string command = targetDistances[minIndex].ToString() + " " + distanceRangeMin.ToString() + " " + distanceRangeMax.ToString();

                        if (charaDiagnostics)
                            Console.WriteLine("Command: {0}", command);

                        // If serial port is open then send the command to the robot
                        if (serialPort.IsOpen)
                        {
                            serialPort.WriteLine(command);
                        }
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("Error: {0}", e.ToString());
                    }
                }
            }
        }

        // Gets invoked when the application is started.
        public void OnApplicationStart()
        {
            try
            {
                // Import and setup configuration variables from HSRobotControl.dll.config
                targetNames = ConfigurationManager.OpenExeConfiguration(Assembly.GetExecutingAssembly().Location).AppSettings.Settings["targetNames"].Value.Split(',');
                targetBoneNames = ConfigurationManager.OpenExeConfiguration(Assembly.GetExecutingAssembly().Location).AppSettings.Settings["targetBoneNames"].Value.Split(',');
                targetPriorities = ConfigurationManager.OpenExeConfiguration(Assembly.GetExecutingAssembly().Location).AppSettings.Settings["targetPriorities"].Value.Split(',');

                string[] values = ConfigurationManager.OpenExeConfiguration(Assembly.GetExecutingAssembly().Location).AppSettings.Settings["targetPriorityRange"].Value.Split(',');
                targetPriorityRange = new float[values.Length];
                for (int i = 0; i < values.Length; i++)
                {
                    targetPriorityRange[i] = Convert.ToSingle(values[i]);
                }

                values = ConfigurationManager.OpenExeConfiguration(Assembly.GetExecutingAssembly().Location).AppSettings.Settings["targetRangeMin"].Value.Split(',');
                targetRangeMin = new float[values.Length];
                for (int i = 0; i < values.Length; i++)
                {
                    targetRangeMin[i] = Convert.ToSingle(values[i]);
                }

                values = ConfigurationManager.OpenExeConfiguration(Assembly.GetExecutingAssembly().Location).AppSettings.Settings["targetRangeMax"].Value.Split(',');
                targetRangeMax = new float[values.Length];
                for (int i = 0; i < values.Length; i++)
                {
                    targetRangeMax[i] = Convert.ToSingle(values[i]);
                }

                targetPriorityAutoRangeTolerance = Convert.ToSingle(ConfigurationManager.OpenExeConfiguration(Assembly.GetExecutingAssembly().Location).AppSettings.Settings["targetPriorityAutoRangeTolerance"].Value);
                targetDistanceRangeThreshold = Convert.ToSingle(ConfigurationManager.OpenExeConfiguration(Assembly.GetExecutingAssembly().Location).AppSettings.Settings["targetDistanceRangeThreshold"].Value);

                serialPortBaudRate = Convert.ToInt32(ConfigurationManager.OpenExeConfiguration(Assembly.GetExecutingAssembly().Location).AppSettings.Settings["serialPortBaudRate"].Value);
                serialPort = new SerialPort(ConfigurationManager.OpenExeConfiguration(Assembly.GetExecutingAssembly().Location).AppSettings.Settings["serialPortName"].Value, serialPortBaudRate);
                autoRange = Convert.ToBoolean(ConfigurationManager.OpenExeConfiguration(Assembly.GetExecutingAssembly().Location).AppSettings.Settings["autoRange"].Value);
                autoRangeTime = Convert.ToSingle(ConfigurationManager.OpenExeConfiguration(Assembly.GetExecutingAssembly().Location).AppSettings.Settings["autoRangeTime"].Value);
                robotUpdateFrequency = Convert.ToSingle(ConfigurationManager.OpenExeConfiguration(Assembly.GetExecutingAssembly().Location).AppSettings.Settings["updateFrequency"].Value);
                charaDiagnostics = Convert.ToBoolean(ConfigurationManager.OpenExeConfiguration(Assembly.GetExecutingAssembly().Location).AppSettings.Settings["charaDiagnostics"].Value);
                configDiagnostics = Convert.ToBoolean(ConfigurationManager.OpenExeConfiguration(Assembly.GetExecutingAssembly().Location).AppSettings.Settings["configDiagnostics"].Value);

                // Setup variables based on current configuration
                int autoRangeLength = (int)(autoRangeTime * robotUpdateFrequency);
                targetAutoRangeValues = new float[autoRangeLength];

                for (int i = 0; i < autoRangeLength; i++)
                {
                    targetAutoRangeValues[i] = targetPriorityRange[1];
                }

                targetPositions = new Vector3[targetNames.Length];
                targetDistances = new float[targetNames.Length];
    }
            catch (Exception e)
            {
                Console.WriteLine("Error: {0}", e.ToString());
            }
        }

        // Gets invoked when the application is closed.
        public void OnApplicationQuit()
        {

        }

        // Gets invoked whenever a level is loaded.
        public void OnLevelWasLoaded(int level)
        {

        }

        // Gets invoked after the first update cycle after a level was loaded.
        public void OnLevelWasInitialized(int level)
        {

        }

        // Gets invoked on every graphic update.
        public void OnUpdate()
        {
            // Open and close the serial port connection when Control+K is pressed on the keyboard
            if ((Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) && Input.GetKeyDown(KeyCode.K))
            {
                try
                {
                    if (serialPort.IsOpen)
                    {
                        try
                        {
                            // Close the serial port connection
                            serialPort.Close();

                            Console.WriteLine("Serial port {0} is closed.", serialPort.PortName);
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine("Error: {0}", e.ToString());
                        }
                    }
                    else
                    {
                        try
                        {
                            // Open the serial port connection
                            serialPort.Open();

                            if (serialPort.IsOpen)
                            {
                                Console.WriteLine("Serial port {0} is open.", serialPort.PortName);
                            }
                            else
                            {
                                Console.WriteLine("Serial port {0} is closed.", serialPort.PortName);
                            }
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine("Error: {0}", e.ToString());
                        }
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine("Error: {0}", e.ToString());
                }
            }

            // Cycle the female index value based on available female chara in the current scene when Control+C is pressed on the keyboard
            if ((Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) && Input.GetKeyDown(KeyCode.C))
            {
                if (femaleIndex + 1 < femaleCount)
                {
                    femaleIndex++;
                }
                else
                {
                    femaleIndex = 0;
                }

                if (femaleIndex >= femaleCount)
                {
                    femaleIndex = 0;
                }

                if (charaDiagnostics)
                    Console.WriteLine("Female chara Index: {0}", femaleIndex);
            }

            // Handles the case when the number of female chara's in the current scene changes
            if (femaleIndex >= femaleCount)
            {
                femaleIndex = 0;
            }

            // Cycle the male index value based on available male chara in the current scene when Shift+C is pressed on the keyboard
            if ((Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) && Input.GetKeyDown(KeyCode.C))
            {
                if (maleIndex + 1 < maleCount)
                {
                    maleIndex++;
                }
                else
                {
                    maleIndex = 0;
                }

                if (maleIndex >= maleCount)
                {
                    maleIndex = 0;
                }

                if (charaDiagnostics)
                    Console.WriteLine("Male chara Index: {0}", maleIndex);
            }

            // Handles the case when the number of male chara's in the current scene changes
            if (maleIndex >= maleCount)
            {
                maleIndex = 0;
            }

            // Get ms elapsed since current stopwatch interval
            float msElapsed = sw.ElapsedMilliseconds;

            // If the ms elapsed is greater than the period based on the robot's update frequency then
            // stop the stopwatch, call the robot update function, and restart the stopwatch
            if (msElapsed >= (1000.0 / robotUpdateFrequency))
            {
                sw.Stop();

                if (configDiagnostics)
                    Console.WriteLine("Time taken: {0}ms, Frequency: {1}Hz", msElapsed, 1000.0 / msElapsed);

                UpdateRobotPosition();

                sw = Stopwatch.StartNew();
            }
        }

        // Gets invoked on ever physics update.
        public void OnFixedUpdate()
        {
            
        }
    }
}
