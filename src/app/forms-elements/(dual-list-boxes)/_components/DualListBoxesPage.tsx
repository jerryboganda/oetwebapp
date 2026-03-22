"use client";
import React, { useEffect, useState } from "react";
import {
  Card,
  CardBody,
  CardHeader,
  Container,
  Row,
  Table,
  UncontrolledCollapse,
} from "reactstrap";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import "prismjs/themes/prism.css";
import { IconCode, IconCreditCard } from "@tabler/icons-react";
import {
  allOptions,
  options,
  options2,
  publicFunctions,
} from "@/Data/FormElements/DuelListBox/DuelListBox";
import TransferListBox from "./TransferListBox";

const DualListBoxesPage: React.FC = () => {
  const [selected, setSelected] = useState<string[]>([]);
  const [selected1, setSelected1] = useState<string[]>([]);
  const [selected2, setSelected2] = useState<string[]>([]);
  const [selected3, setSelected3] = useState<string[]>([]);
  useEffect(() => {
    if (typeof window !== "undefined") {
      import("prismjs").then((Prism) => {
        Prism.highlightAll();
      });
    }
  }, []);

  return (
    <div>
      <Container fluid>
        <Breadcrumbs
          mainTitle="Dual List Boxes"
          title="Forms elements"
          path={["Dual list boxes"]}
          Icon={IconCreditCard}
        />
        <Row>
          <div className="col-xxl-6">
            <Card>
              <CardHeader className="code-header">
                <h5>Select by class</h5>
                <a href="#" id="togglerDuelLiastBox">
                  <IconCode
                    data-source="blockbtn"
                    className="source"
                    size={32}
                  />
                </a>
              </CardHeader>
              <CardBody>
                <TransferListBox
                  leftItems={options.map((opt) =>
                    typeof opt === "string" ? opt : opt.label || opt.value
                  )}
                  rightItems={selected}
                  setRightItems={setSelected}
                  addText="Add"
                  removeText="Remove"
                  addAllText="Add All"
                  removeAllText="Remove All"
                />
                <UncontrolledCollapse toggler="#togglerDuelLiastBox">
                  <pre className="dlb1 mt-3">
                    <code className="language-js">{`
<TransferListBox
  leftItems={['One', 'Two', 'Three']}
  rightItems={selected}
  setRightItems={setSelected}
  addText="Add"
  removeText="Remove"
  addAllText="Add All"
  removeAllText="Remove All"
/>
  `}</code>
                  </pre>
                </UncontrolledCollapse>
              </CardBody>
            </Card>
          </div>
          <div className="col-xxl-6">
            <Card>
              <CardHeader className="code-header">
                <h5>Select by class</h5>
                <a href="#" id="togglerDuelLiastBox1">
                  <IconCode
                    data-source="blockbtn"
                    className="source"
                    size={32}
                  />
                </a>
              </CardHeader>
              <CardBody>
                <div className="selected-element d-flex mb-2 f-w-600">
                  <label>Selected element :&nbsp;</label>
                  <ul className="changed-element text-primary">
                    <li> Nothing yet</li>
                  </ul>
                </div>
                <TransferListBox
                  leftItems={options.map((opt) =>
                    typeof opt === "string" ? opt : opt.label || opt.value
                  )}
                  rightItems={selected1}
                  setRightItems={setSelected1}
                  addText=">"
                  removeText="<"
                  addAllText=">>"
                  removeAllText="<<"
                />

                <UncontrolledCollapse toggler="#togglerDuelLiastBox1">
                  <pre className="dlb2 mt-3">
                    <code className="language-js">{`
<TransferListBox
  leftItems={['One', 'Two', 'Three']}
  rightItems={selected1}
  setRightItems={setSelected1}
  leftTitle="Available numbers"
  rightTitle="Selected numbers"
  addText=">"
  removeText="<"
  addAllText=">>"
  removeAllText="<<"
/>
  `}</code>
                  </pre>
                </UncontrolledCollapse>
              </CardBody>
            </Card>
          </div>
          <div className="col-xxl-6">
            <Card>
              <CardHeader className="code-header">
                <h5>Select by class</h5>
                <a href="#" id="togglerDuelLiastBox2">
                  <IconCode
                    data-source="blockbtn"
                    className="source"
                    size={32}
                  />
                </a>
              </CardHeader>
              <CardBody>
                <div className="selected-element d-flex mb-2 f-w-600">
                  <label>Selected element :&nbsp;</label>
                  <ul className="changed-element text-primary">
                    <li> Nothing yet</li>
                  </ul>
                </div>
                <TransferListBox
                  leftItems={options.map((opt) =>
                    typeof opt === "string" ? opt : opt.label || opt.value
                  )}
                  rightItems={selected2}
                  setRightItems={setSelected2}
                  leftTitle="Available options"
                  rightTitle="Selected options"
                />

                <UncontrolledCollapse toggler="#togglerDuelLiastBox2">
                  <pre className="dlb3 mt-3">
                    <code className="language-js">{`
<TransferListBox
  leftItems={['One', 'Two', 'Three']}
  rightItems={selected2}
  setRightItems={setSelected2}
/>
  `}</code>
                  </pre>
                </UncontrolledCollapse>
              </CardBody>
            </Card>
          </div>
          <div className="col-xxl-6">
            <Card>
              <CardHeader className="code-header">
                <h5>Show the sort buttons</h5>
                <a href="#" id="togglerDuelLiastBoxSort">
                  <IconCode className="source" data-source="dlb4" size={32} />
                </a>
              </CardHeader>
              <CardBody>
                <TransferListBox
                  leftItems={options2.map((opt) =>
                    typeof opt === "string" ? opt : opt.label || opt.value
                  )}
                  rightItems={selected3}
                  setRightItems={setSelected3}
                  leftTitle="Available options"
                  rightTitle="Selected options"
                  addText="🤌"
                  addAllText="🤌"
                  removeText="🤌"
                  removeAllText="🤌"
                />

                <UncontrolledCollapse toggler="#togglerDuelLiastBoxSort">
                  <pre className="dlb4 mt-3">
                    <code className="language-js">{`
<TransferListBox
  leftItems={['One', 'Two', 'Three', 'Four', 'Five', 'Six', 'Seven', 'Eight', 'Nine']}
  rightItems={selected3}
  setRightItems={setSelected3}
  leftTitle="Available options"
  rightTitle="Selected options"
  addText="🤌"
  addAllText="🤌"
  removeText="🤌"
  removeAllText="🤌"
/>
  `}</code>
                  </pre>
                </UncontrolledCollapse>
              </CardBody>
            </Card>
          </div>
          <div className="col-md-12">
            <Card>
              <CardHeader>
                <h5>All options</h5>
              </CardHeader>
              <CardBody>
                <div className="table-responsive">
                  <Table className="table-bottom-border table-hover mb-0">
                    <thead>
                      <tr>
                        <th>Option</th>
                        <th>Default</th>
                        <th>Excepted values</th>
                        <th>Explanation</th>
                      </tr>
                    </thead>
                    <tbody>
                      {allOptions.map(([opt, def, type, exp]) => (
                        <tr key={opt}>
                          <th>{opt}</th>
                          <td>
                            <code>{def}</code>
                          </td>
                          <td>
                            <code>{type}</code>
                          </td>
                          <td>{exp}</td>
                        </tr>
                      ))}
                    </tbody>
                  </Table>
                </div>
              </CardBody>
            </Card>

            <Card className="mt-4">
              <CardHeader>
                <h5>Public functions</h5>
              </CardHeader>
              <CardBody>
                <div className="table-responsive">
                  <Table className="table-bottom-border table-hover mb-0">
                    <thead>
                      <tr>
                        <th>Function name</th>
                        <th>Arguments</th>
                        <th>Explanation</th>
                      </tr>
                    </thead>
                    <tbody>
                      {publicFunctions.map(([name, args, desc]) => (
                        <tr key={name}>
                          <th>{name}</th>
                          <td>{args}</td>
                          <td>{desc}</td>
                        </tr>
                      ))}
                    </tbody>
                  </Table>
                </div>
              </CardBody>
            </Card>
          </div>
        </Row>
      </Container>
    </div>
  );
};

export default DualListBoxesPage;
