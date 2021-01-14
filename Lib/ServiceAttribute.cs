using System;

namespace KCert.Lib
{
    public class ServiceAttribute : Attribute
    {
        public Type Type { get; set; }
    }
}
