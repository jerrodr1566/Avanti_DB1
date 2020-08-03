using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace _1172_Avanti_NewBusiness_DB1
{
    class Program
    {
        static void Main(string[] args)
        {
            bool TestMode = false;

            try
            {
                _1172_Avanti_NewBusiness_DB1 oNB = new _1172_Avanti_NewBusiness_DB1(TestMode);
            }
            catch (Exception Ex)
            {
                Console.WriteLine("Error encountered:\n" + Ex.Message);
                Console.WriteLine("Press any key to continue.");
                Console.ReadLine();
            }
            
        }
    }
}
