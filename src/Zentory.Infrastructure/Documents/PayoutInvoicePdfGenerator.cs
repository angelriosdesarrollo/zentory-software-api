using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Zentory.Application.Common.Interfaces;

namespace Zentory.Infrastructure.Documents;

public sealed class PayoutInvoicePdfGenerator : IPayoutInvoicePdfGenerator
{
    public byte[] Generate(PayoutInvoicePdfModel model)
    {
        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(40);
                page.DefaultTextStyle(x => x.FontSize(11));

                page.Header().Column(col =>
                {
                    col.Spacing(4);
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Column(header =>
                        {
                            header.Item().Text("Cuenta de cobro").FontSize(20).Bold();
                            header.Item().Text(model.LegalName ?? model.CompanyName).FontSize(12).SemiBold();
                            if (!string.IsNullOrWhiteSpace(model.Nit))
                                header.Item().Text($"NIT: {model.Nit}").FontSize(9).FontColor(Colors.Grey.Darken1);
                        });

                        if (model.LogoBytes is { Length: > 0 })
                            row.ConstantItem(70).Height(70).Image(model.LogoBytes).FitArea();
                    });

                    var contactParts = new[]
                    {
                        model.Address, model.City, model.Email, model.Phone,
                    }.Where(p => !string.IsNullOrWhiteSpace(p));
                    var contactLine = string.Join(" · ", contactParts);
                    if (!string.IsNullOrWhiteSpace(contactLine))
                        col.Item().Text(contactLine).FontSize(9).FontColor(Colors.Grey.Darken1);

                    col.Item().PaddingTop(6).LineHorizontal(0.75f).LineColor(Colors.Grey.Lighten2);
                });

                page.Content().PaddingVertical(20).Column(col =>
                {
                    col.Spacing(10);

                    col.Item().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(12).Column(info =>
                    {
                        info.Spacing(4);
                        info.Item().Text($"Colaborador: {model.CollaboratorName}");
                        if (!string.IsNullOrWhiteSpace(model.CollaboratorIdNumber))
                            info.Item().Text($"Identificación: {model.CollaboratorIdNumber}");
                        info.Item().Text($"Período: {model.Period}");
                        info.Item().Text($"Concepto: {model.Concept}");
                        info.Item().Text($"Fecha de emisión: {model.IssuedAt:dd/MM/yyyy}");
                    });

                    col.Item().Background(Colors.Blue.Lighten5).Padding(14).Row(row =>
                    {
                        row.RelativeItem().Text("Valor a pagar").FontSize(11).FontColor(Colors.Blue.Darken2);
                        row.AutoItem().Text($"{model.Amount:N0} {model.Currency}").FontSize(18).Bold().FontColor(Colors.Blue.Darken2);
                    });

                    if (model.SignedByName is not null && model.SignedAt is not null)
                    {
                        col.Item().PaddingTop(10).Border(1).BorderColor(Colors.Green.Lighten2)
                            .Background(Colors.Green.Lighten5).Padding(12).Column(sig =>
                        {
                            sig.Spacing(3);
                            sig.Item().Text("Firma electrónica").FontSize(10).Bold().FontColor(Colors.Green.Darken2);
                            sig.Item().Text($"Firmado por: {model.SignedByName}").FontSize(10);
                            sig.Item().Text($"Fecha y hora: {model.SignedAt:dd/MM/yyyy HH:mm} UTC").FontSize(10);
                            sig.Item().Text("Firmado electrónicamente a través de Zentory.").FontSize(8.5f).FontColor(Colors.Grey.Darken1);
                        });
                    }
                });

                page.Footer().AlignCenter().Text("Generado con Zentory").FontSize(9).FontColor(Colors.Grey.Medium);
            });
        });

        return document.GeneratePdf();
    }
}
