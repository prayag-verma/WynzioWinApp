using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Wynzio.Services.Security
{
    internal interface ISecurityService
    {
        // Define interface methods
        string EncryptData(string data);
        string DecryptData(string encryptedData);
        bool ValidateConnection(string connectionId);
    }
}