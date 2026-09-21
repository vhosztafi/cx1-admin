"""Independent text/geometry check of the OperationalPdfTests outputs (pypdf)."""
import argparse
from pathlib import Path
from decimal import Decimal
import json
from pypdf import PdfReader

parser = argparse.ArgumentParser()
parser.add_argument("directory", type=Path)
args = parser.parse_args()
expected = {
    "commercial-combined-policy-certificate.pdf",
    "commercial-combined-policy-schedule.pdf",
    "motor-trade-combined-statement-of-fact.pdf",
    "motor-trade-road-risks-policy-schedule.pdf",
    "motor-trade-road-risks-policy-certificate.pdf",
    "motor-trade-road-risks-cancellation-notice.pdf",
    "commercial-combined-cancellation-notice.pdf",
    "motor-trade-road-risks-endorsement.pdf",
    "commercial-combined-endorsement.pdf",
}
for name in sorted(expected):
    path = args.directory / name
    pdf = PdfReader(path)
    pages = [page.extract_text() for page in pdf.pages]
    text = "\n".join(pages)
    assert "Café Noël" in text and "DEMONSTRATION ONLY" in text, name
    assert "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa" in text, name
    assert "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb" in text, name
    assert "Question labels SHA-256" in text, name
    assert all("Page " in page for page in pages), name
    assert all(abs(float(page.mediabox.width) - 595.28) < 1 and abs(float(page.mediabox.height) - 841.89) < 1 for page in pdf.pages), name
    assert "prototype.quote." not in text and "Client Agency Relationship Id" not in text, name
    if "cancellation-notice" in name:
        assert "insured-request" in text, name
        assert "this notice does not calculate a refund" in " ".join(text.split()), name
        assert "Premium and charges" not in text and "Selected cover" not in text, name
    elif "endorsement" in name:
        wording = "Maintain the declared alarm protection." if "commercial" in name else "Use only the declared vehicles for the recorded business activities."
        assert wording in " ".join(text.split()), name
        assert "Selected endorsements" in text, name
    elif "commercial" in name:
        assert "Drivers" not in text and "Motor cover" not in text, name
        assert "£10,000,000.00" in text, name
        assert "Maintain the declared alarm protection." in text, name
        if "schedule" in name:
            assert "LONG-DECLARATION-START" in text and "LONG-DECLARATION-END" in text, name
            assert " ".join(text.split()).count("Keep the declared fire watch") == 30, name
    elif "policy-certificate" in name:
        assert "Jamie Example" in text and "DEMO 01" in text, name
        assert "own-vehicles" in text and "£10,000.00" in text and "£500.00" in text, name
        assert "Requested options" not in text, name
    else:
        assert "Jamie Example" in text and "Comprehensive" in text, name
        assert "£25,000.00" in text, name
    path.with_suffix(".txt").write_text(text, encoding="utf-8")
    print(f"{name}: {len(pdf.pages)} A4 pages; independent expected content passed")

for name in ("real-sql-motor-quotation", "real-sql-commercial-quotation"):
    path = args.directory / (name + ".pdf")
    expected_terms = json.loads(path.with_suffix(".expected.json").read_text(encoding="utf-8-sig"))
    pdf = PdfReader(path)
    text = "\n".join(page.extract_text() for page in pdf.pages)
    normalized = " ".join(text.split())
    assert expected_terms["insuredName"] in normalized, name
    assert expected_terms["agencyName"] in normalized, name
    assert f"£{Decimal(expected_terms['grossPayable']):,.2f}" in normalized, name
    assert expected_terms["termsId"] in text, name
    assert expected_terms["termsHash"] in "".join(text.split()), name
    assert "this quotation does not issue cover" in normalized, name
    for wording in expected_terms["conditions"]:
        assert " ".join(wording.split()) in normalized, name
    if "commercial" in name:
        assert "Drivers" not in text and "Motor cover" not in text, name
    path.with_suffix(".txt").write_text(text, encoding="utf-8")
    print(f"{name}: {len(pdf.pages)} pages; independent retained SQL price, parties, conditions and terms identity passed")

for name in ("real-sql-motor-trade-road-risks-renewal", "real-sql-motor-trade-combined-renewal", "real-sql-commercial-renewal", "real-sql-motor-adjustment"):
    path = args.directory / (name + ".pdf")
    expected_terms = json.loads(path.with_suffix(".expected.json").read_text(encoding="utf-8-sig"))
    pdf = PdfReader(path)
    text = "\n".join(page.extract_text() for page in pdf.pages)
    normalized = " ".join(text.split())
    assert f"£{Decimal(expected_terms['grossPayable']):,.2f}" in normalized, name
    assert expected_terms["termsId"] in text, name
    assert expected_terms["termsHash"] in "".join(text.split()), name
    assert "does not change or renew issued cover" in normalized, name
    for date in expected_terms["dates"]:
        assert "Proposed from " + date in normalized, name
    if "commercial" in name:
        assert "Drivers" not in text and "Motor cover" not in text, name
    if "adjustment" in name:
        assert len(expected_terms["dates"]) == 2, name
    path.with_suffix(".txt").write_text(text, encoding="utf-8")
    print(f"{name}: {len(pdf.pages)} pages; independent retained SQL price, effective slices and terms identity passed")
