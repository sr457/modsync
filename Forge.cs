﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Diagnostics;
using System.Reflection;
using EnterpriseDT.Net.Ftp;

namespace modsync
{
    static class Forge
    {
        public static void Check(ref FTPConnection ftpcon)
        {
            // return if disabled
            if ((Config.settings.ForgeVersion == "") || (Config.settings.ForgeDownloadFile == ""))
            {
                return;
            }

            // check forge json file
            if (File.Exists(Locations.LocalFolderName_Versions + "\\" + Config.settings.ForgeVersion + "\\" + Config.settings.ForgeVersion + ".json"))
            {
                Console.WriteLine(Strings.Get("ForgeOK"));
            }
            else
            {
                Install(ref ftpcon);
            }

            UpdateProfile("Forge", Config.settings.ForgeVersion);
        }
        
        static void Install(ref FTPConnection ftpcon)
        {
            // create default profile if missing
            if (!File.Exists(Locations.LauncherProfiles))
            {
                UpdateProfile("(Default)", Config.settings.MinecraftVersion);
            }
            
            string LocalFile = Locations.LocalFolderName_TempDir + "\\" + Config.settings.ForgeDownloadFile;
            string RemoteFile = Config.ftpsettings.FtpServerFolder + "/" + Config.settings.ForgeDownloadFile;
            try
            {
                // download and install forge
                Console.WriteLine(Strings.Get("ForgeDownload"));
                File.Delete(LocalFile);
                ftpcon.DownloadFile(LocalFile, RemoteFile);
                Console.WriteLine(Strings.Get("ForgeInstall"));

                if (File.Exists(Locations.Java))
                {
                    // extract wrapper class to invoke client install
                    Resources.ExtractFile(Locations.LocalFolderName_TempDir, "ForgeInstallWrapper.class");
                    ProcessStartInfo psi = new ProcessStartInfo();
                    psi.FileName = Locations.Java;
                    psi.WorkingDirectory = Path.GetDirectoryName(LocalFile);
                    psi.Arguments = "-cp \"" + Locations.LocalFolderName_TempDir + "\" ForgeInstallWrapper \"" + Config.settings.ForgeDownloadFile + "\" \"" + Locations.LocalFolderName_Minecraft + "\"";
                    psi.UseShellExecute = false;
                    var process = Process.Start(psi);
                    process.WaitForExit();
                    File.Delete(LocalFile);
                    File.Delete(Locations.LocalFolderName_TempDir + "\\" + "ForgeInstallWrapper.class");
                }
                else
                {
                    Process.Start(LocalFile);
                    // exit without starting game
                    Program.Exit(false);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(Strings.Get("ForgeError") + ex.Message);
                Console.WriteLine(Strings.Get("PressKey"));
                Console.ReadKey();
                Program.Exit(true);
            }
        }

        #region Profile File

        // updates launcher_profiles.json to use a specific profile, game version and java location
        static void UpdateProfile(string profile, string version)
        {
            try
            {
                string file;
                bool write, exists;
                if (File.Exists(Locations.LauncherProfiles))
                {
                    file = File.ReadAllText(Locations.LauncherProfiles, Encoding.ASCII);
                    write = false;
                    exists = true;
                }
                else
                {
                    file = DefaultProfile();
                    write = true;
                    exists = false;
                }

                CheckExistingProfile(ref file, ref write, profile);
                CheckSelectedProfile(ref file, ref write, profile);
                CheckProfileKey(ref file, ref write, profile, "lastVersionId", version, Strings.Get("ProfileGameVersion"));
                if (File.Exists(Locations.Javaw))
                {
                    CheckProfileKey(ref file, ref write, profile, "javaDir", Locations.Javaw.Replace("\\", "\\\\"), Strings.Get("ProfileJavaVersion"));
                }

                if (write)
                {
                    if (exists)
                    {
                        File.Copy(Locations.LauncherProfiles, Locations.LauncherProfiles + ".org", true);
                    }
                    File.WriteAllText(Locations.LauncherProfiles, file, Encoding.ASCII);
                }
            }
            catch (Exception)
            {
                Console.WriteLine(Strings.Get("ProfileError") + version);
                Console.WriteLine(Strings.Get("PressKey"));
                Console.ReadKey();
                Program.Exit(true);
            }
        }

        // returns a default profile file
        static string DefaultProfile()
        {
            return "{\n  \"profiles\": {\n    \"(Default)\": {\n      \"name\": \"(Default)\"\n    }\n  },\n  \"selectedProfile\": \"(Default)\"\n}";
        }

        // checks if a profile exists, or adds it
        static void CheckExistingProfile(ref string file, ref bool write, string profile)
        {
            string str_search = "\"name\": \"" + profile + "\"";
            int idx_start = file.IndexOf(str_search);
            if (idx_start < 0)
            {
                // add profile
                string str_search_prof = "\"profiles\": {";
                idx_start = file.IndexOf(str_search_prof);
                if (idx_start < 0)
                {
                    // bad file
                    return;
                }
                idx_start += str_search_prof.Length;
                string str_profile = "\n    \"" + profile + "\": {\n      " + str_search + "\n    },";
                file = file.Substring(0, idx_start) + str_profile + file.Substring(idx_start, file.Length - idx_start);
                write = true;
                Console.WriteLine(Strings.Get("Profile") + profile + Strings.Get("ProfileAdded"));
            }
        }

        // checks if a specific profile is selected, or adds the line
        static void CheckSelectedProfile(ref string file, ref bool write, string profile)
        {
            string str_search = "\"selectedProfile\": \"";
            int idx_start = file.IndexOf(str_search);
            if (idx_start >= 0)
            {
                idx_start += str_search.Length;
                int idx_end = file.IndexOf("\"", idx_start);
                if (idx_end >= 0)
                {
                    string curprofile = file.Substring(idx_start, idx_end - idx_start);
                    if (curprofile != profile)
                    {
                        file = file.Substring(0, idx_start) + profile + file.Substring(idx_end, file.Length - idx_end);
                        write = true;
                        Console.WriteLine(Strings.Get("Profile") + profile + Strings.Get("ProfileSelected"));
                    }
                }
            }
        }

        // checks if a key exists in a profile and is set to a specific value, or adds/updates the key
        static void CheckProfileKey(ref string file, ref bool write, string profile, string key, string value, string keystr)
        {
            string str_search = "\"name\": \"" + profile + "\"";
            int idx_start = file.IndexOf(str_search);
            int idx_end, idx_profend;

            if (idx_start >= 0)
            {
                // profile found, look for key
                idx_start += str_search.Length;
                str_search = "\"" + key + "\": \"";
                idx_end = file.IndexOf(str_search, idx_start);
                idx_profend = file.IndexOf("}", idx_start);

                // if key found before profile end
                if ((idx_end >= 0) && (idx_end < idx_profend))
                {
                    // update existing key
                    idx_start = idx_end + str_search.Length;
                    idx_end = file.IndexOf("\"", idx_start);
                    if (idx_end >= 0)
                    {
                        string version = file.Substring(idx_start, idx_end - idx_start);
                        if (version != value)
                        {
                            file = file.Substring(0, idx_start) + value + file.Substring(idx_end, file.Length - idx_end);
                            write = true;
                            Console.WriteLine(Strings.Get("Profile") + keystr + value);
                        }
                    }
                }
                else
                {
                    // insert new key
                    string str_write = ",\n      " + str_search + value + "\"";
                    file = file.Substring(0, idx_start) + str_write + file.Substring(idx_start, file.Length - idx_start);
                    write = true;
                    Console.WriteLine(Strings.Get("Profile") + keystr + value);
                }
            }
        }

        #endregion
    }
}
