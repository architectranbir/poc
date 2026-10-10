using LedgerPilot.Domain;
namespace LedgerPilot.Domain.Tests;
public class CommercialCalculationsTests
{
 static ExchangeRate Rate(string to="GBP",decimal rate=1m)=>new("GBP",to,rate,new DateOnly(2026,10,10),"Synthetic policy v1");
 [Fact] public void StandardVatReconcilesGross(){var p=CommercialCalculations.Preview(100m,"GBP","GBP",Rate(),VatTreatment.Standard,.2m);Assert.Equal(20m,p.Vat);Assert.Equal(120m,p.Gross);}
 [Fact] public void AlternateCurrencyUsesExactDecimalRate(){var p=CommercialCalculations.Preview(123.45m,"GBP","EUR",Rate("EUR",1.15m),VatTreatment.Standard,.2m);Assert.Equal(141.97m,p.BillingNet);Assert.Equal(28.39m,p.Vat);Assert.Equal(170.36m,p.Gross);}
 [Theory] [InlineData(VatTreatment.Exempt)] [InlineData(VatTreatment.ReverseCharge)]
 public void ZeroVatTreatmentsPreserveNet(VatTreatment treatment){var p=CommercialCalculations.Preview(10m,"GBP","GBP",Rate(),treatment,0m);Assert.Equal(0m,p.Vat);Assert.Equal(p.BillingNet,p.Gross);}
 [Fact] public void MidpointUsesDeclaredMoneyPolicy(){var p=CommercialCalculations.Preview(.005m,"GBP","GBP",Rate(),VatTreatment.Standard,0m);Assert.Equal(.01m,p.BillingNet);}
 [Theory] [InlineData(0)] [InlineData(-1)] public void NonPositiveFxRejected(int rate){Assert.Throws<ArgumentException>(()=>CommercialCalculations.Preview(1,"GBP","EUR",Rate("EUR",rate),VatTreatment.Exempt,0));}
 [Fact] public void WrongPairRejected(){Assert.Throws<ArgumentException>(()=>CommercialCalculations.Preview(1,"GBP","USD",Rate("EUR",1.15m),VatTreatment.Exempt,0));}
 [Fact] public void SameCurrencyCannotIntroduceFx(){Assert.Throws<ArgumentException>(()=>CommercialCalculations.Preview(1,"GBP","GBP",Rate("GBP",2),VatTreatment.Exempt,0));}
 [Theory] [InlineData(VatTreatment.Exempt)] [InlineData(VatTreatment.ReverseCharge)] public void ZeroTreatmentCannotChargeVat(VatTreatment treatment){Assert.Throws<ArgumentException>(()=>CommercialCalculations.Preview(1,"GBP","GBP",Rate(),treatment,.2m));}
 [Fact] public void NegativeBillRejected(){Assert.Throws<ArgumentOutOfRangeException>(()=>CommercialCalculations.Preview(-1,"GBP","GBP",Rate(),VatTreatment.Exempt,0));}
}
