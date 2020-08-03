using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Data;
using System.IO;
using System.Data.Odbc;
using AmcClientLibrary;
using System.Text.RegularExpressions;
using System.Threading;
using System.Globalization;
using Microsoft.SqlServer.Server;
using System.Security.Cryptography.X509Certificates;

namespace _1172_Avanti_NewBusiness_DB1
{
    class _1172_Avanti_NewBusiness_DB1
    {
        AMC_Functions.GeneralFunctions oGenFun = new AMC_Functions.GeneralFunctions();

        // used primarily for the connection string
        string Author = "jerrodr";                                                                                // Citrix username of the person created (used if specific ports are used)                                              
        bool TestMode;                                                                                            // flag for test mode or not                                                                                            

        string ClientName = "Avanti Billing Company";                                                               // client name                     
        string ClientGroup = "AVANTI-G";                                                                             // creditor group all client numbers are included                                                                       
        //G:\Clients\Woodlawn Hospital\Physician\Data
        string FilePath = @"\\AMC-FS1\pubshares\Public\Clients\Avanti\New Business\";       // main client folder path - DOES NOT NEED Data in it                                                                   
        string FileName = "*";  // file name looking for - can contain asteriks, but not needed, need the main words looking for (ex: Client_ASSIGN)     (will change with live file)
        string FileExt = ".xlsx";                                                                                  // file extention looking for (typically .txt)                                                                          
        string LayoutFile = @"\\AMC-FS1\pubshares\Public\Clients\Avanti\New Business\DONOTDELETE_NBLAYOUT.xlsx";          // file path of the layout file - this will not change with test flag being modified                                    
        string DataBase = "DB1";                                                                                  // database used for duplicate - should be DB1 or DB5                                                                   
        bool epicFile = false;                                                                                     // flag as to whether it's an epic file or not (matters because of record based   

        DataTable dtData = new DataTable("Data");                                                                       // combined data table of RPT1 AND RPT2
        DataTable dtFinal = new DataTable("final");                                                                      // data table if needed to modify any in the final after


        public _1172_Avanti_NewBusiness_DB1(bool inTestMode)
        {
            TestMode = inTestMode;

            JAR_NewBusiness oNB = new JAR_NewBusiness(TestMode, ClientName, Author, ClientGroup, FilePath, FileName, FileExt, LayoutFile, DataBase);

            // read the data in, since they're in different layouts partially, and need to be modified
            ReadAllData(oNB);
            
            if (dtData.Rows.Count >= 1)
            {
                // pass the data back in so we can keep this rolling in our library
                oNB.TransferDTBack(dtData);

                // update data as needed via the library
                oNB.EvaluateData_TransferToFinal(DataBase, false, true, true, 60);

                // get the final sheet out, and make any necessary adjustments to the data before passing back in for the rest of the functions
                dtFinal = oNB.returnTableToModify_Final();

                FinalizeNB();

                oNB.TransferDTBack_Final(dtFinal);

                // each of these should be included, just need to toggle the flag whether they're used or not
                // start doing the special things, if they are set to true
                oNB.Best2Phones();                                                                                                                 // find the best 2 phones, and make sure they're all valid
                oNB.AD1_LengthScrub();                                                                                                             // if AD1 is too long, load the rest to AD2
                oNB.ScrubSSN();                                                                                                                    // scrub socials that aren't valid
                oNB.Find501R(DataBase, true);                                                                                                      // 501R date
                oNB.AlphaCheck(false);
                oNB.Note_Deduper();                                                                                                                // remove duplicate notes as needed across CLA#
                oNB.NBPH();                                                                                                                        // NBPH magic - same on EBO/DB1 (as soon as EBO's is live)
                oNB.MakeACMT(DataBase, false);                                                                                                            // create the ACMT line, based off of which fields are loaded 
                oNB.DateUpdater(DataBase, true);                                                                                                  // set to false due to dodChecker after
                oNB.GuarIsMinor(true);                                                                                                            // determine if the guarantor is a minor
                oNB.DupeCheck();                                                                                                                   // duplicate check, by default is a hard dupe
                oNB.SmallBal("CLA#", 0.00, true);                                                                                                  // small balance removal (used same logic from old woodlawn)

                oNB.SplitTableResults();                                                                                                           // move the tables into their appropriate tables (small bal and dupes, and final to pass through)

                if (!inTestMode)
                {
                    JAR_NewBusinessFunctions.SendClientReports(dtFinal, oNB.dt_Saver, ClientName, ClientGroup, DataBase, TestMode, FilePath, null, null, null, null, null, null, null, null, true);
                }

                oNB.CallNBForm(null, TestMode);
            }
            else
            {
                Console.WriteLine("\nNo data was found to process.\nPlease check the data folder and try again.\n\nPress any key or close the process to continue.");
                Console.ReadLine();
                Environment.Exit(1);
            }
        }

        /// <summary>
        /// Read the data in in order to get them correct, since the data is different occassionally and need it to all be the same
        /// </summary>
        /// <param name="oNB">Passed NB, need it of the layout</param>
        private void ReadAllData(JAR_NewBusiness oNB)
        {
            DataSet temp_DS = new DataSet();
            DirectoryInfo rootDir;

            foreach (DataRow dr in oNB.dt_NBHeaders.Rows)
            {
                dtData.Columns.Add(dr["Header"].ToString());
            }

            // add the file from as well
            dtData.Columns.Add("FileFrom");

            // read in the data, and get set to add them in as we can
            List<FileInfo> temp_FileList = new List<FileInfo>();

            if (TestMode)
            {
                rootDir = new DirectoryInfo(FilePath + "Test\\Data\\");
            }
            else
            {
                rootDir = new DirectoryInfo(FilePath + "Data\\");
            }
            

            temp_FileList.AddRange(rootDir.GetFiles(FileName + FileExt, SearchOption.TopDirectoryOnly));

            foreach (FileInfo file in temp_FileList)
            {
                if (!file.Name.Contains("~$" + file.Name))
                {
                    temp_DS = oGenFun.ReadExcelFile_AllFiles(file.FullName, false, null);

                    // check if the first row is blank, if so, need to skip that row and get the headers as they are in the file
                    foreach (DataTable dt in temp_DS.Tables)
                    {
                        if (dt.Rows.Count >= 0)
                        {
                            bool clearfirstRow = true;
                            bool useTable = true;

                            do
                            {
                                // check the column count, if it's the same as the ones in the NB headers, then we can go forward, otherwise can't use that sheet (incomplete) (rows in dt_Headers since it goes vertical)
                                if (dt.Columns.Count >= oNB.dt_NBHeaders.Rows.Count)
                                {
                                    // check to make sure that at least 3 of the columns have data, since the others usually only have a possible initial, and then file name
                                    int totalUnblankinFirst = 0;

                                    foreach (DataColumn dc in dt.Columns)
                                    {
                                        if (dt.Rows[0][dc.ColumnName].ToString() != string.Empty)
                                        {
                                            totalUnblankinFirst++;
                                        }
                                    }

                                    if (totalUnblankinFirst >= 3)
                                    {
                                        // we're good here, not considered a blank row so can keep it
                                        clearfirstRow = false;

                                    }
                                    else
                                    {
                                        // delete that row and keep going
                                        dt.Rows.RemoveAt(0);
                                    }
                                }
                                else
                                {
                                    // columns don't match, so exit the loop and flag to ignore
                                    clearfirstRow = false;
                                    useTable = false;
                                }
                            } while (clearfirstRow == true);

                            if (useTable)
                            {
                                // now that we're out of that, check the first 2 columns, if they consist of ACCT and LAST NAME, or LAST NAME and FIRST NAME, assign that to be the headers, unless it came in automatically in the first row then don't change them
                                if ((dt.Rows[0][0].ToString().ToUpper() == "ACCT #" && dt.Rows[0][1].ToString().ToUpper() == "LAST NAME") || (dt.Rows[0][0].ToString().ToUpper() == "LAST NAME" && dt.Rows[0][1].ToString().ToUpper() == "FIRST NAME"))
                                {
                                    int currentColCount = 0;

                                    foreach (DataColumn dc in dt.Columns)
                                    {
                                        if (dc.ColumnName != "FileFrom")
                                        {
                                            dc.ColumnName = dt.Rows[0][currentColCount].ToString();
                                            currentColCount++;
                                        }
                                    }

                                    // remove the first row, as we don't need it now
                                    dt.Rows.RemoveAt(0);
                                }

                                // now, go through each and add the appropriate data as per expected in the layout, based off of header text
                                foreach (DataRow dr in dt.Rows)
                                {
                                    DataRow drToAdd = dtData.NewRow();

                                    foreach (DataRow drHeader in oNB.dt_NBHeaders.Rows)
                                    {
                                        drToAdd[drHeader["Header"].ToString()] = dr[drHeader["Header"].ToString()].ToString();
                                    }

                                    drToAdd["FileFrom"] = dr["FileFrom"].ToString();

                                    // add it in
                                    dtData.Rows.Add(drToAdd);
                                }
                            }

                        }                        
                    }
                }
            }
        }


        /// <summary>
        /// Take any of the weird format ones and make sure the name itself is all good.
        /// </summary>
        private void FinalizeNB()
        {
            foreach (DataRow dr in dtFinal.Rows)
            {
                // format the dates so they don't have time stamps at the end
                if (dr["DOB"].ToString() != string.Empty)
                {
                    dr["DOB"] = Convert.ToDateTime(dr["DOB"].ToString()).ToString("MM/dd/yyyy");
                }

                if (dr["ADATA:AT2"].ToString() != string.Empty)
                {
                    dr["ADATA:AT2"] = Convert.ToDateTime(dr["ADATA:AT2"].ToString()).ToString("MM/dd/yyyy");
                }

                if (dr["LAD"].ToString() != string.Empty)
                {
                    dr["LAD"] = Convert.ToDateTime(dr["LAD"].ToString()).ToString("MM/dd/yyyy");
                }

                if (dr["ADATA:AU5"].ToString() != string.Empty)
                {
                    dr["ADATA:AU5"] = Convert.ToDateTime(dr["ADATA:AU5"].ToString()).ToString("MM/dd/yyyy");
                }

                if (dr["ADATA:AU6"].ToString() != string.Empty)
                {
                    dr["ADATA:AU6"] = Convert.ToDateTime(dr["ADATA:AU6"].ToString()).ToString("MM/dd/yyyy");
                }

                dr["PATIENT"] = JAR_NewBusinessFunctions.RemoveExtraSpaces(dr["PATIENT"].ToString().Trim());
                dr["ADATA:AH1"] = JAR_NewBusinessFunctions.RemoveExtraSpaces(dr["ADATA:AH1"].ToString().Trim());

                // check if fn/ln is not blank, if so then take the data, split by space, and assign that, otherwise take whatever is in the patient data
                if (dr["xxFN"].ToString() != string.Empty || dr["xxLN"].ToString() != string.Empty)
                {
                    var nameSplit = JAR_NewBusinessFunctions.returnNameParser(dr["xxFN"].ToString());

                    dr["FN"] = JAR_NewBusinessFunctions.RemoveExtraSpaces((nameSplit.First + " " + nameSplit.Middle).Trim());
                    dr["LN"] = JAR_NewBusinessFunctions.RemoveExtraSpaces((nameSplit.Last + " " + nameSplit.Suffix).Trim());
                }
                else 
                {
                    // they are blank, so take the patient fn and ln
                    dr["FN"] = JAR_NewBusinessFunctions.RemoveExtraSpaces((dr["XXPTFN"].ToString() + " " + dr["XXPTMI"].ToString()).Trim());
                    dr["LN"] = JAR_NewBusinessFunctions.RemoveExtraSpaces((dr["XXPTLN"].ToString()).Trim());
                }

            }
        }
    }
}
