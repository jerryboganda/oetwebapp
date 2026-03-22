import React from "react";
import { Card, CardBody, CardHeader, Col, Table } from "reactstrap";
import { FontAwesomeIcon } from "@fortawesome/react-fontawesome";
import {
  faLessThan,
  faGreaterThanEqual,
} from "@fortawesome/free-solid-svg-icons";
import { IconDefinition } from "@fortawesome/fontawesome-svg-core";

// --- TYPES ---
type Breakpoint = {
  name: string;
  icon: IconDefinition;
  value: string;
};

type TextOrHTMLRow = {
  label: string;
  type: "text" | "html";
  values: string[];
  className?: string;
};

type ColspanRow = {
  label: string;
  type: "colspan";
  value: string;
  className?: string;
};

type CustomRow = {
  label: string;
  type: "custom";
  values: { text: string; colSpan?: number }[];
  className?: string;
};

type GridRow = TextOrHTMLRow | ColspanRow | CustomRow;

// --- DATA ---
const breakpoints: Breakpoint[] = [
  { name: "Extra small", icon: faLessThan, value: "576px" },
  { name: "Small", icon: faGreaterThanEqual, value: "576px" },
  { name: "Medium", icon: faGreaterThanEqual, value: "768px" },
  { name: "Large", icon: faGreaterThanEqual, value: "992px" },
  { name: "Extra large", icon: faGreaterThanEqual, value: "1200px" },
  { name: "Extra extra large", icon: faGreaterThanEqual, value: "1400px" },
];

const gridRows: GridRow[] = [
  {
    label: "Grid behavior",
    type: "custom",
    values: [
      { text: "Horizontal at all times" },
      {
        text: "Collapsed to start, horizontal above breakpoints",
        colSpan: 5,
      },
    ],
    className: "app-grid-box",
  },
  {
    label: "Max container width",
    type: "text",
    values: ["None (auto)", "540px", "720px", "960px", "1140px", "1320px"],
  },
  {
    label: "Class prefix",
    type: "html",
    values: [
      ".col-",
      ".col-sm-",
      ".col-md-",
      ".col-lg-",
      ".col-xl-",
      ".col-xxl-",
    ],
    className: "app-grid-box text-danger",
  },
  {
    label: "# of columns",
    type: "colspan",
    value: "12",
  },
  {
    label: "Gutter width",
    type: "colspan",
    value: "1.5rem (.75rem on left and right)",
    className: "app-grid-box",
  },
  {
    label: "Nestable",
    type: "colspan",
    value: "Yes",
  },
  {
    label: "Offsets",
    type: "colspan",
    value: "Yes",
    className: "app-grid-box",
  },
  {
    label: "Column ordering",
    type: "colspan",
    value: "Yes",
  },
];

// --- COMPONENT ---
const GridOptionsTable: React.FC = () => {
  return (
    <Col xs={12}>
      <Card>
        <CardHeader>
          <h5>Grid Options</h5>
          <p className="mt-1 f-m-light">
            Bootstrap grid system allows all six breakpoints, and any
            breakpoints you can customize.
          </p>
        </CardHeader>
        <CardBody>
          <div className="table-responsive">
            <Table className="table table-secondary">
              <thead className="grids">
                <tr>
                  <th></th>
                  {breakpoints.map((bp, i) => (
                    <th key={i} className="app-grid">
                      {bp.name}
                      <br />
                      <small>
                        <FontAwesomeIcon icon={bp.icon} className="w-10" />{" "}
                        {bp.value}
                      </small>
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody className="grids">
                {gridRows.map((row, index) => (
                  <tr key={index} className={row.className || ""}>
                    <th className="text-nowrap fw-600" scope="row">
                      {row.label}
                    </th>

                    {row.type === "text" &&
                      row.values.map((val, i) => <td key={i}>{val}</td>)}

                    {row.type === "html" &&
                      row.values.map((val, i) => (
                        <td key={i}>
                          <div className="text-danger">{val}</div>
                        </td>
                      ))}

                    {row.type === "colspan" && <td colSpan={6}>{row.value}</td>}

                    {row.type === "custom" &&
                      row.values.map((val, i) => (
                        <td key={i} colSpan={val.colSpan || 1}>
                          {val.text}
                        </td>
                      ))}
                  </tr>
                ))}
              </tbody>
            </Table>
          </div>
        </CardBody>
      </Card>
    </Col>
  );
};

export default GridOptionsTable;
