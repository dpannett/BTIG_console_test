using System;
using System.Configuration;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Text;

namespace BTIG_console_test
{
    class Program
    {
        // NB. SQL connection string specified in App.config
        //const string connectionString = "Server=localhost;Database=BTIG_sample;Trusted_Connection=True;";

        private const string usagePrompt = "Usage: BTIG_console_test [dir_path | --1:max_words | --2:ceiling | --3 | --?]";
        private static readonly char[] splitChars = { ' ', '\n', '\r', '\t', ',', '.', '?', '!', '\'', '"', (char)0x1A, (char)0x2A, (char)0x60, (char)0xB4, '(', ')', '[', ']', '-', ':', ';', '_' };

        static void Main(string[] args)
        {
            if (args == null || args.Length == 0 || String.IsNullOrEmpty(args[0]) || args[0] == "--?")
            {
                Console.WriteLine(usagePrompt);
                return;
            }
            Program p = new Program();
            if (args[0].Length > 2 && args[0].Substring(0, 2) == "--")
                p.ParseCommandLine(args[0]);
            else
                p.ProcessDirectory(args[0]);
        }

        private Program() { }

        private void ParseCommandLine(string commandParam)
        {
            switch (commandParam.Substring(2, 1))
            {
                case "1":
                    GetTopWords(commandParam);
                    break;
                case "2":
                    GetRollingCharCount(commandParam);
                    break;
                case "3":
                    GetCharCountByWords();
                    break;
                default:
                    Console.WriteLine(usagePrompt);
                    break;
            }
        }

        private void ProcessDirectory(string dirPath)
        {
            if (!Directory.Exists(dirPath))
            {
                Console.WriteLine("Directory " + dirPath + " not found!");
                return;
            }

            string[] files = Directory.GetFiles(dirPath);
            if (files.Length == 0)
            {
                Console.WriteLine("Directory " + dirPath + " is empty.");
                return;
            }

            foreach (string filePath in files)
            {
                if (File.Exists(filePath))
                    ProcessFile(filePath);
                // We could process subdirectories recursively here, like this:
                /*
                if (Directory.Exists(filePath))
                    ProcessDirectory(filePath);
                */
            }
        }

        private void ProcessFile(string filePath)
        {
            DateTime startTime = DateTime.Now;
            string text = File.ReadAllText(filePath, Encoding.Default);
            string[] words = text.Split(splitChars, StringSplitOptions.RemoveEmptyEntries);
            string[] lines = text.Split(new char[] { '\n' }, StringSplitOptions.None);
            Console.WriteLine("File " + filePath + String.Format(" CharCount = {0}, WordCount = {1}", text.Length, words.Length));
            UpdateFile(lines, filePath);
            string updatedText = File.ReadAllText(filePath, Encoding.Default);
            LogFileProcessing(filePath, updatedText, startTime);
            LogCharCounts(filePath, updatedText);
            LogWordCounts(filePath, updatedText);
        }

        private void UpdateFile(string[] lines, string filePath)
        {
            for (int i = 0; i < lines.Length; ++i)
            {
                if (String.IsNullOrWhiteSpace(lines[i]))
                    ScanForReversal(lines, i);
                else
                    lines[i] = lines[i].Replace('a', (char)228);
            }
            File.WriteAllLines(filePath, lines, Encoding.Default);
        }

        private void ScanForReversal(string[] lines, int i)
        {
            // called when lines[i] is blank
            if (i < lines.Length - 1)
            {
                // find any words starting with T and ending with E, then reverse them
                string[] words = lines[i + 1].Split(splitChars, StringSplitOptions.RemoveEmptyEntries);
                foreach (string word in words)
                {
                    if (word.StartsWith("T", StringComparison.CurrentCultureIgnoreCase) && word.EndsWith("E", StringComparison.CurrentCultureIgnoreCase))
                        lines[i + 1] = lines[i + 1].Replace(word, ReverseString(word));
                }
            }
        }

        private string ReverseString(string s)
        {
            char[] chars = s.ToCharArray();
            Array.Reverse(chars);
            return new String(chars);
        }

        private void LogFileProcessing(string filePath, string updatedText, DateTime startTime)
        {
            string connectionString = ConfigurationManager.ConnectionStrings["BTIG_sample"].ConnectionString;
            using (SqlConnection sqlConnection = new SqlConnection(connectionString))
            {
                sqlConnection.Open();
                const string sql = "insert into BTIG_sample..Processed (filePath, updatedText, startTime) values ('{0}', '{1}', '{2}')";
                using (SqlCommand command = new SqlCommand(String.Format(sql, filePath, updatedText.Replace("'", "''"), startTime), sqlConnection))
                {
                    command.ExecuteNonQuery();
                }
            }
        }

        private void LogCharCounts(string filePath, string updatedText)
        {
            // case sensitive char counts
            Dictionary<char, int> charCounts = new Dictionary<char, int>();
            foreach (char c in updatedText.ToCharArray())
            {
                if (!charCounts.ContainsKey(c))
                    charCounts.Add(c, 1);
                else
                    ++charCounts[c];
            }

            const string sql = "insert into BTIG_sample..CharCount (filePath, charValue, charCount) values ('{0}', '{1}', '{2}');";
            StringBuilder sb = new StringBuilder();
            foreach (KeyValuePair<char, int> keyValuePair in charCounts)
            {
                if (keyValuePair.Key == '\'')
                    sb.AppendFormat(sql, filePath, "''", keyValuePair.Value);
                else if (keyValuePair.Key != (char)0x0A && keyValuePair.Key != (char)0x1A) // NB. These control characters caused a mismatch between stored procedure #2 and the console-displayed character count.
                    sb.AppendFormat(sql, filePath, keyValuePair.Key, keyValuePair.Value);
            }

            string connectionString = ConfigurationManager.ConnectionStrings["BTIG_sample"].ConnectionString;
            using (SqlConnection sqlConnection = new SqlConnection(connectionString))
            {
                sqlConnection.Open();
                using (SqlCommand command = new SqlCommand(sb.ToString(), sqlConnection))
                {
                    command.ExecuteNonQuery();
                }
            }
        }

        private void LogWordCounts(string filePath, string updatedText)
        {
            // case insensitive word counts
            string[] words = updatedText.Split(splitChars, StringSplitOptions.RemoveEmptyEntries);
            Dictionary<string, int> wordCounts = new Dictionary<string, int>();
            foreach (string word in words)
            {
                if (String.IsNullOrWhiteSpace(word))
                    continue;
                string s = word.ToLower().Replace("'", "''");
                if (!wordCounts.ContainsKey(s))
                    wordCounts.Add(s, 1);
                else
                    ++wordCounts[s];
            }

            const string sql = "insert into BTIG_sample..WordCount (filePath, word, wordCount) values ('{0}', '{1}', '{2}');";
            StringBuilder sb = new StringBuilder();
            foreach (KeyValuePair<string, int> keyValuePair in wordCounts)
            {
                sb.AppendFormat(sql, filePath, keyValuePair.Key, keyValuePair.Value);
            }

            string connectionString = ConfigurationManager.ConnectionStrings["BTIG_sample"].ConnectionString;
            using (SqlConnection sqlConnection = new SqlConnection(connectionString))
            {
                sqlConnection.Open();
                using (SqlCommand command = new SqlCommand(sb.ToString(), sqlConnection))
                {
                    command.ExecuteNonQuery();
                }
            }
        }

        private void GetTopWords(string commandParam)
        {
            int maxWords = ParseParam(commandParam);
            GetTopWords(maxWords);
        }

        private void GetTopWords(int maxWords)
        {
            if (maxWords < 0)
            {
                Console.WriteLine("Please note that parameter 'maxWords' is required to call this procedure.");
                Console.WriteLine(usagePrompt);
                return;
            }

            string connectionString = ConfigurationManager.ConnectionStrings["BTIG_sample"].ConnectionString;
            using (SqlConnection sqlConnection = new SqlConnection(connectionString))
            {
                sqlConnection.Open();
                using (SqlCommand command = new SqlCommand("BTIG_sample..uspGetTopWords", sqlConnection))
                {
                    command.CommandType = CommandType.StoredProcedure;
                    command.Parameters.Add(new SqlParameter("@maxWords", maxWords));
                    using (SqlDataReader reader = command.ExecuteReader(CommandBehavior.SingleResult))
                    {
                        if (reader.HasRows)
                        {
                            StringBuilder sb = new StringBuilder();
                            sb.AppendLine("filePath,word,wordCount");
                            while (reader.Read())
                                sb.AppendLine(String.Format("{0},{1},{2}", reader[0], reader[1], reader[2]));
                            File.WriteAllText(".\\TopWords.csv", sb.ToString(), UnicodeEncoding.UTF8);
                        }
                        else
                            Console.WriteLine("No rows found.");
                    }
                }
            }
        }

        private void GetRollingCharCount(string commandParam)
        {
            int ceiling = ParseParam(commandParam);
            GetRollingCharCount(ceiling);
        }

        private void GetRollingCharCount(int ceiling)
        {
            if (ceiling < 0)
            {
                Console.WriteLine("Please note that parameter 'ceiling' is required to call this procedure.");
                Console.WriteLine(usagePrompt);
                return;
            }

            string connectionString = ConfigurationManager.ConnectionStrings["BTIG_sample"].ConnectionString;
            using (SqlConnection sqlConnection = new SqlConnection(connectionString))
            {
                sqlConnection.Open();
                using (SqlCommand command = new SqlCommand("BTIG_sample..uspGetRollingCharCount", sqlConnection))
                {
                    command.CommandType = CommandType.StoredProcedure;
                    command.Parameters.Add(new SqlParameter("@ceiling", ceiling));
                    using (SqlDataReader reader = command.ExecuteReader(CommandBehavior.SingleResult))
                    {
                        if (reader.HasRows)
                        {
                            StringBuilder sb = new StringBuilder();
                            sb.AppendLine("filePath,charValue,runningTotal,ranking");
                            while (reader.Read())
                                sb.AppendLine(String.Format("{0},{1},{2},{3}", reader[0], reader[1].ToString().TrimEnd('\n', '\r', '\t', (char)0x2A, (char)0x60, (char)0xB4).Replace("\"", "\\\"").Replace(",", "\",\""), reader[2], reader[3])); // CSV-related fixups
                            File.WriteAllText(".\\RollingCharCount.csv", sb.ToString(), UnicodeEncoding.UTF8);
                        }
                        else
                            Console.WriteLine("No rows found.");
                    }
                }
            }
        }

        private int ParseParam(string commandParam)
        {
            int param;
            if (commandParam.Length <= 4 || commandParam.Substring(3, 1) != ":" || !Int32.TryParse(commandParam.Substring(4), out param))
                return -1;
            else
                return param;
        }

        private void GetCharCountByWords()
        {
            string connectionString = ConfigurationManager.ConnectionStrings["BTIG_sample"].ConnectionString;
            using (SqlConnection sqlConnection = new SqlConnection(connectionString))
            {
                sqlConnection.Open();
                using (SqlCommand command = new SqlCommand("BTIG_sample..uspGetCharCountByWords", sqlConnection))
                {
                    command.CommandType = CommandType.StoredProcedure;
                    using (SqlDataReader reader = command.ExecuteReader(CommandBehavior.SingleResult))
                    {
                        if (reader.HasRows)
                        {
                            StringBuilder sb = new StringBuilder();
                            sb.AppendLine("filePath,word,charCount");
                            while (reader.Read())
                                sb.AppendLine(String.Format("{0},{1},{2}", reader[0], reader[1], reader[2]));
                            File.WriteAllText(".\\CharCountByWords.csv", sb.ToString(), UnicodeEncoding.UTF8);
                        }
                        else
                            Console.WriteLine("No rows found.");
                    }
                }
            }
        }
    }
}
