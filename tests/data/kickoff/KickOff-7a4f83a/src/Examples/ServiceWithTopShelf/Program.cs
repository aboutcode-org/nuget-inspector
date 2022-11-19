using System;
using System.Threading;
using KickOff;
using KickOff.Stages;
using Topshelf;

namespace ServiceWithTopShelf
{
	class Program
	{
		static void Main(string[] args)
		{
			HostFactory.Run(x =>
			{
				x.Service<Pipeline>(s =>
				{
					s.ConstructUsing(name => new Pipeline(new IStage[]
					{
						// new StructureMapStage(),
						// ...
						// ...
						new AsyncRunnerStage(),
					}));
					s.WhenStarted(pipeline => pipeline.OnStart(args));
					s.WhenStopped(pipeline => pipeline.OnStop());
				});
			});
		}
	}

	public class App : IStartup
	{
		public void Execute(ServiceArgs service)
		{
			Console.WriteLine("Running!");

			while (service.CancelRequested == false)
			{
				Thread.Sleep(400);
				Console.Write(".");
			}

			Console.WriteLine();
		}
	}
}
