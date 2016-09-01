using GoodAI.Modules.NeuralNetwork.Tasks;
using GoodAI.Core.Utils;
using GoodAI.Core.Execution;
using NDesk.Options;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Globalization;

namespace GoodAI.CoreRunner
{
    class ExampleExperiment
    {
        public static void Run(string[] args)
        {
            // -clusterid $(Cluster) -processid $(Process) -brain Breakout.brain -factor 0.5

            double discountFactor = 0.6;
            string breakoutBrainFilePath = "";
            OptionSet options = new OptionSet()
                .Add("factor=", v => discountFactor = Double.Parse(v, CultureInfo.InvariantCulture))
                .Add("brain=", v => breakoutBrainFilePath = Path.GetFullPath(v));

            try
            {
                options.Parse(Environment.GetCommandLineArgs().Skip(1));
            }
            catch (OptionException e)
            {
                MyLog.ERROR.WriteLine(e.Message);
            }

            MyProjectRunner runner = new MyProjectRunner(MyLogLevel.DEBUG);
            StringBuilder result = new StringBuilder();
            runner.OpenProject(breakoutBrainFilePath);
            runner.DumpNodes();
            runner.SaveOnStop(23, true);
            for (int i = 0; i < 5; ++i)
            {
                runner.RunAndPause(1000, 100);
                float[] data = runner.GetValues(23, "Bias");
                MyLog.DEBUG.WriteLine(data[0]);
                MyLog.DEBUG.WriteLine(data[1]);
                result.AppendFormat("{0}: {1}, {2}", i, data[0], data[1]);
                runner.Set(23, typeof(MyQLearningTask), "DiscountFactor", discountFactor);
                runner.RunAndPause(1000, 300);
                data = runner.GetValues(23, "Bias");
                MyLog.DEBUG.WriteLine(data[0]);
                MyLog.DEBUG.WriteLine(data[1]);
                result.AppendFormat(" --- {0}, {1}", data[0], data[1]).AppendLine();
                runner.Reset();
            }

            string resultFilePath = @"res.txt";
            File.WriteAllText(resultFilePath, result.ToString());
            string brainzFilePath = @"state.brainz";
            runner.SaveProject(brainzFilePath);

            runner.Shutdown();
        }
    }
}
