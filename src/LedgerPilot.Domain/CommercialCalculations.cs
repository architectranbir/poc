namespace LedgerPilot.Domain;

public enum VatTreatment { Standard, Exempt, ReverseCharge }
public sealed record ExchangeRate(string SourceCurrency,string TargetCurrency,decimal Rate,DateOnly AsOf,string Source);
public sealed record CommercialPreview(decimal ProjectNet,string ProjectCurrency,decimal BillingNet,
    string BillingCurrency,decimal Vat,decimal Gross,VatTreatment Treatment,ExchangeRate ExchangeRate);

public static class CommercialCalculations
{
    public static CommercialPreview Preview(decimal projectNet,string projectCurrency,string billingCurrency,
        ExchangeRate exchangeRate,VatTreatment treatment,decimal vatRate)
    {
        if(projectNet<0)throw new ArgumentOutOfRangeException(nameof(projectNet));
        if(string.IsNullOrWhiteSpace(projectCurrency)||string.IsNullOrWhiteSpace(billingCurrency))throw new ArgumentException("Currencies required.");
        if(exchangeRate.SourceCurrency!=projectCurrency||exchangeRate.TargetCurrency!=billingCurrency||exchangeRate.Rate<=0||string.IsNullOrWhiteSpace(exchangeRate.Source))
            throw new ArgumentException("A matching positive exchange rate and source are required.");
        if(projectCurrency==billingCurrency&&exchangeRate.Rate!=1m)throw new ArgumentException("Same-currency rate must be one.");
        if(vatRate<0||vatRate>1)throw new ArgumentOutOfRangeException(nameof(vatRate));
        if(!Enum.IsDefined(treatment))throw new ArgumentException("Unknown VAT treatment.");
        if(treatment!=VatTreatment.Standard&&vatRate!=0m)throw new ArgumentException("Exempt and reverse-charge VAT must have zero rate.");
        var net=BillingCalculations.RoundMoney(projectNet*exchangeRate.Rate);
        var vat=BillingCalculations.RoundMoney(net*vatRate);
        return new(projectNet,projectCurrency,net,billingCurrency,vat,net+vat,treatment,exchangeRate);
    }
}
