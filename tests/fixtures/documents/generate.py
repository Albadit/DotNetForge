# Writes the sample Office files of the media preview tests (.docs/guides/testing.md). They come from python-docx,
# openpyxl and python-pptx rather than from hand-written XML, so the reader is tested against files it had no part in.
# Only needed to change them: pip install python-docx openpyxl python-pptx, then python generate.py
import datetime
import io
import pathlib
import struct
import zlib

from docx import Document
from openpyxl import Workbook
from pptx import Presentation
from pptx.chart.data import CategoryChartData
from pptx.dml.color import RGBColor
from pptx.enum.chart import XL_CHART_TYPE
from pptx.enum.shapes import MSO_SHAPE
from pptx.util import Inches, Pt

HERE = pathlib.Path(__file__).parent


def png(size):
    """An orange square."""
    def chunk(name, data):
        return struct.pack(">I", len(data)) + name + data + struct.pack(">I", zlib.crc32(name + data))

    rows = (b"\x00" + b"\xff\x66\x00" * size) * size
    header = struct.pack(">IIBBBBB", size, size, 8, 2, 0, 0, 0)
    return b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", header) + chunk(b"IDAT", zlib.compress(rows)) + chunk(b"IEND", b"")


PNG = png(8)


def word():
    document = Document()
    document.add_heading("Quarterly report", level=0)
    document.add_heading("Summary", level=1)
    paragraph = document.add_paragraph("Sales grew ")
    paragraph.add_run("twelve percent").bold = True
    paragraph.add_run(" in the ")
    paragraph.add_run("third").italic = True
    paragraph.add_run(" quarter.")
    document.add_paragraph("First point", style="List Bullet")
    document.add_paragraph("Second point", style="List Bullet")
    document.add_paragraph("Step one", style="List Number")
    document.add_paragraph("Step two", style="List Number")
    table = document.add_table(rows=2, cols=2)
    table.cell(0, 0).text = "Region"
    table.cell(0, 1).text = "Sales"
    table.cell(1, 0).text = "North"
    table.cell(1, 1).text = "1200"
    document.add_picture(io.BytesIO(PNG), width=Inches(1))
    document.save(HERE / "sample.docx")


def excel():
    workbook = Workbook()
    sales = workbook.active
    sales.title = "Sales"
    sales.append(["Region", "Sales", "Share", "Closed on"])
    sales.append(["North", 1200.5, 0.25, datetime.date(2026, 3, 31)])
    sales.append(["South", 800, 0.75, datetime.date(2026, 6, 30)])
    sales["C2"].number_format = "0%"
    sales["C3"].number_format = "0%"
    sales["D2"].number_format = "yyyy-mm-dd"
    sales["D3"].number_format = "yyyy-mm-dd"
    notes = workbook.create_sheet("Notes")
    notes["A1"] = "Checked by finance"
    notes["C3"] = "Far corner"
    workbook.create_sheet("Hidden").sheet_state = "hidden"
    workbook.save(HERE / "sample.xlsx")


def powerpoint():
    presentation = Presentation()
    title = presentation.slides.add_slide(presentation.slide_layouts[0])
    title.shapes.title.text = "Quarterly review"
    title.placeholders[1].text = "Third quarter"

    bullets = presentation.slides.add_slide(presentation.slide_layouts[1])
    bullets.shapes.title.text = "Highlights"
    frame = bullets.placeholders[1].text_frame
    frame.text = "Sales grew"
    frame.add_paragraph().text = "Costs fell"
    nested = frame.add_paragraph()
    nested.text = "Mostly shipping"
    nested.level = 1

    blank = presentation.slides.add_slide(presentation.slide_layouts[6])
    blank.background.fill.solid()
    blank.background.fill.fore_color.rgb = RGBColor(0x10, 0x20, 0x30)
    box = blank.shapes.add_shape(MSO_SHAPE.RECTANGLE, Inches(1), Inches(1), Inches(3), Inches(1))
    box.fill.solid()
    box.fill.fore_color.rgb = RGBColor(0xFF, 0x66, 0x00)
    box.text_frame.text = "Filled box"
    run = box.text_frame.paragraphs[0].runs[0]
    run.font.size = Pt(24)
    run.font.color.rgb = RGBColor(0xFF, 0xFF, 0xFF)
    blank.shapes.add_picture(io.BytesIO(PNG), Inches(5), Inches(1), Inches(1), Inches(1))
    table = blank.shapes.add_table(2, 2, Inches(1), Inches(3), Inches(4), Inches(1)).table
    table.cell(0, 0).text = "Region"
    table.cell(0, 1).text = "Sales"
    table.cell(1, 0).text = "North"
    table.cell(1, 1).text = "1200"

    chart = presentation.slides.add_slide(presentation.slide_layouts[5])
    chart.shapes.title.text = "Trend"
    data = CategoryChartData()
    data.categories = ["Q1", "Q2"]
    data.add_series("Sales", (1, 2))
    chart.shapes.add_chart(XL_CHART_TYPE.COLUMN_CLUSTERED, Inches(1), Inches(2), Inches(4), Inches(3), data)

    hidden = presentation.slides.add_slide(presentation.slide_layouts[5])
    hidden.shapes.title.text = "Not for the audience"
    hidden._element.set("show", "0")
    presentation.save(HERE / "sample.pptx")


word()
excel()
powerpoint()
