import React from "react";
import { Card, CardBody, CardHeader, Col, Row } from "reactstrap";

interface ColumnConfig {
  className: string;
  content: string;
}

interface Section {
  title: string;
  rows: ColumnConfig[][];
}

const sections: Section[] = [
  {
    title: "Stacked To Horizontal",
    rows: [
      [
        { className: "col-sm-8", content: "col-sm-8" },
        { className: "col-sm-4", content: "col-sm-4" },
      ],
      Array(3).fill({ className: "col-sm", content: "col-sm" }),
    ],
  },
  {
    title: "Mix And Match",
    rows: [
      [
        { className: "col-sm-8", content: "col-sm-8" },
        { className: "col-6 col-md-4", content: "col-6 col-md-4" },
      ],
      Array(3).fill({ className: "col-6 col-md-4", content: "col-6 col-md-4" }),
      [
        { className: "col-6", content: "col-6" },
        { className: "col-6", content: "col-6" },
      ],
    ],
  },
  {
    title: "Nesting",
    rows: [], // handled manually since nesting requires special structure
  },
  {
    title: "Horizontal alignment",
    rows: [
      Array(2).fill({ className: "col-4", content: "One of two columns" }),
      Array(2).fill({ className: "col-4", content: "One of two columns" }),
      Array(2).fill({ className: "col-4", content: "One of two columns" }),
    ],
  },
  {
    title: "Vertical alignment",
    rows: [], // requires nested column-specific alignment classes
  },
  {
    title: "Offset classes",
    rows: [
      [
        { className: "col-md-4", content: ".col-md-4" },
        {
          className: "col-md-4 offset-md-4",
          content: ".col-md-4 .offset-md-4",
        },
      ],
      [
        {
          className: "col-md-3 offset-md-3",
          content: ".col-md-3 .offset-md-3",
        },
        {
          className: "col-md-3 offset-md-3",
          content: ".col-md-3 .offset-md-3",
        },
      ],
      [
        {
          className: "col-md-6 offset-md-3",
          content: ".col-md-6 .offset-md-3",
        },
      ],
    ],
  },
  {
    title: "Margin Utilities",
    rows: [
      [
        { className: "col-md-4", content: ".col-md-4" },
        { className: "col-md-4 ms-auto", content: ".col-md-4 .ms-auto" },
      ],
      [
        { className: "col-md-3 ms-md-auto", content: ".col-md-3 .ms-md-auto" },
        { className: "col-md-3 ms-md-auto", content: ".col-md-3 .ms-md-auto" },
      ],
      [
        { className: "col-auto me-auto", content: ".col-auto .me-auto" },
        { className: "col-auto", content: ".col-auto" },
      ],
    ],
  },
];

const GridExamples = () => {
  return (
    <>
      {sections.map((section, sIdx) => (
        <Col xs={12} key={sIdx} className="mb-4">
          <Card>
            <CardHeader>
              <h5 className="card-title">{section.title}</h5>
            </CardHeader>
            <CardBody>
              {section.rows.map((row, rIdx) => (
                <Row
                  key={rIdx}
                  className={
                    section.title.includes("alignment")
                      ? `justify-content-${["start", "center", "end"][rIdx] || "start"} p-2`
                      : "mt-3 p-2"
                  }
                >
                  {row.map((col, cIdx) => (
                    <div key={cIdx} className={col.className}>
                      <div className="text-center p-2 bg-light-secondary b-r-22 mb-3">
                        {col.content}
                      </div>
                    </div>
                  ))}
                </Row>
              ))}

              {/* Handle manually nested layout for "Nesting" */}
              {section.title === "Nesting" && (
                <Row className="text-center">
                  <div className="col-sm-3">
                    <div className="text-center p-2 bg-light-secondary b-r-22 mb-3">
                      Level 1: .col-sm-3
                    </div>
                  </div>
                  <div className="col-sm-9">
                    <Row>
                      <div className="col-8 col-sm-6">
                        <div className="text-center p-2 bg-light-secondary b-r-22 mb-3">
                          Level 2: .col-8 .col-sm-6
                        </div>
                      </div>
                      <div className="col-4 col-sm-6">
                        <div className="text-center p-2 bg-light-secondary b-r-22 mb-3">
                          Level 2: .col-4 .col-sm-6
                        </div>
                      </div>
                    </Row>
                  </div>
                </Row>
              )}

              {/* Vertical alignment section - handled manually */}
              {section.title === "Vertical alignment" && (
                <Row className="align-items-start">
                  {["start", "center", "end"].map((align, i) => (
                    <div className={`col align-self-${align}`} key={i}>
                      {[1, 2].map((j) => (
                        <div
                          key={j}
                          className="text-center p-2 bg-light-secondary b-r-22 mb-3"
                        >
                          One of three columns
                        </div>
                      ))}
                    </div>
                  ))}
                </Row>
              )}
            </CardBody>
          </Card>
        </Col>
      ))}
    </>
  );
};

export default GridExamples;
