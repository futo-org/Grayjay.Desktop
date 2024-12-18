using Grayjay.ClientServer.Payment;

namespace Grayjay.Desktop.Tests
{
    [TestClass]
    public class LicenseValidatorTests
    {
        [TestMethod]
        public void Test_Validate()
        {
            var validator = new LicenseValidator("MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEAqyDuxsRtD5gmBoLCNoZaXSRTwyUxgzcPHzLZkvomXVSQqzD+3aOKngcTKAZ83rm4GvoyMlBukxQMLShannSxk8GQGTCT7VStQKNc4lKVER5ASB6aEaypaFMIYI3rXN1xLF1LqY/j7cu5GgMsvAuUVYFBexYFF6xcC5JDBZW6Pw/KYoJm3rswFixjPMGESmZRFCjjdAkHk47BhRPFBlvzwv9Ez1stdHcTpa/odEXIeJWIsZk9DHtCNCZyt6B6FXojVzrXsF2TxCNHGcHhlX43ALgQikiRcof1FsxoewTQhjLwMiDqB02mHCdRxssdnW3xadqyK678kQKfoIB1KB2N/QIDAQAB");
            Assert.IsTrue(validator.Validate("UX8Z-9D7F-8BPX-8Q9J-WD7W-2PH1-D55A-QUG5", "VjpUEu20t4yW_Qmc3_Gji8FXmBfa95OFNx15Zf7OCJFxw1Kq2XF_Rv0dg4EdWkGwVaHzulIsDz6_ndTEhTWKCPm7kh4kT2VAkf8LCEwLjp5w7sz46Lb1jltA2orDt6UH90uKlT516VmsTXlEd9kIQ72Yt3jqzyI8JIG7Q1P5zj_e-wQ8XOyAJNiRVz-Ot0Xe-3qtufyaIbpyVut-OCAPEpHVaVUNHCDM9EuintQxuSZe_zwOaoFIR1hLnTKuc-xizO_da5qhWKjfnd5Dl4xwH-1ff47s7ltUavYI4ovcIC4LpSV84_5VBAsUr84B8fwNqu-b6AWRL5N1wkollIBzAg"));
        }
    }
}
