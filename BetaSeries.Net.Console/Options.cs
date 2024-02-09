using CommandLine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BetaSeries.Net.Console
{
    internal class Options
    {
        //[Option('v', "verbose", Required = false, HelpText = "Enable verbose output.")]
        //public bool Verbose { get; set; }

        [Option('M', "skip-movies", Required = false, HelpText = "Skip movies migration")]
        public bool SkipMovies { get; set; }

        [Option('S', "skip-shows", Required = false, HelpText = "Skip shows migration")]
        public bool SkipShows { get; set; }
    }
}
