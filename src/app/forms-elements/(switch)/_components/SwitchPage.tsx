"use client";
import React, { useState } from "react";
import { Card, CardBody, CardHeader, Col, Container, Row } from "reactstrap";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import { IconCreditCard } from "@tabler/icons-react";

interface SwitchState {
  [key: string]: boolean;
}
const SwitchPage = () => {
  const [basicSwitch1, setBasicSwitch1] = useState<boolean>(false);
  const [basicSwitch2, setBasicSwitch2] = useState<boolean>(false);
  const [switch001, setSwitch001] = useState<boolean>(true);
  const [switch002, setSwitch002] = useState<boolean>(false);
  const [switch003, setSwitch003] = useState<boolean>(true);
  const [switch004, setSwitch004] = useState<boolean>(false);
  const [switch005, setSwitch005] = useState<boolean>(true);
  const [switch006, setSwitch006] = useState<boolean>(false);

  const handleSwitchChange = (
    setter: React.Dispatch<React.SetStateAction<boolean>>
  ) => {
    setter((prev) => !prev);
  };

  const [switchStates, setSwitchStates] = useState<SwitchState>({
    check5: true,
    check6: true,
    check7: true,
    check8: true,
    check9: true,
    check10: true,
    check01: true,
  });

  const handleSwitchChange1 = (id: string) => {
    setSwitchStates((prevState) => ({
      ...prevState,
      [id]: !prevState[id],
    }));
  };

  const [switchStates2, setSwitchStates2] = useState<SwitchState>({
    check02: true,
    check03: true,
    check04: true,
    check05: true,
    check06: true,
    check07: true,
    check08: true,
  });

  const handleSwitchChange2 = (id: string) => {
    setSwitchStates2((prevState) => ({
      ...prevState,
      [id]: !prevState[id],
    }));
  };

  return (
    <div>
      <Container fluid>
        <Breadcrumbs
          mainTitle="switch"
          title="Forms elements"
          path={["switch"]}
          Icon={IconCreditCard}
        />
        <Row>
          <Col sm="12">
            <Card>
              <CardHeader>
                <h5>Basic Switch</h5>
              </CardHeader>
              <CardBody className="main-switch">
                <div className="form-check form-switch d-flex">
                  <input
                    className="form-check-input"
                    type="checkbox"
                    id="basic-switch-1"
                    checked={basicSwitch1}
                    onChange={() => handleSwitchChange(setBasicSwitch1)}
                  />
                  <label
                    className="form-check-label ms-2"
                    htmlFor="basic-switch-1"
                  >
                    Bootstrap Switch
                  </label>
                </div>
                <div className="form-check form-switch d-flex">
                  <input
                    className="form-check-input"
                    type="checkbox"
                    id="basic-switch-2"
                    checked={basicSwitch2}
                    onChange={() => handleSwitchChange(setBasicSwitch2)}
                    disabled
                  />
                  <label
                    className="form-check-label ms-2"
                    htmlFor="basic-switch-2"
                  >
                    Bootstrap Disabled
                  </label>
                </div>
              </CardBody>
            </Card>
          </Col>

          <Col sm="6" xxl="4">
            <Card className="equal-card">
              <CardHeader>
                <h5>Switch Sizing</h5>
              </CardHeader>
              <CardBody className="main-switch main-switch-color">
                <div className="switch-primary my-3">
                  <input
                    type="checkbox"
                    id="check-001"
                    className="toggle"
                    checked={switch001}
                    onChange={() => handleSwitchChange(setSwitch001)}
                  />
                  <label htmlFor="check-001" className="text-dark">
                    Large Size
                  </label>
                </div>
                <div className="switch-secondary my-3">
                  <input
                    type="checkbox"
                    id="check-002"
                    className="toggle"
                    checked={switch002}
                    onChange={() => handleSwitchChange(setSwitch002)}
                  />
                  <label htmlFor="check-002"> Large Unchecked</label>
                </div>
                <div className="swich-size my-3">
                  <input
                    type="checkbox"
                    id="check-003"
                    className="toggle"
                    checked={switch003}
                    onChange={() => handleSwitchChange(setSwitch003)}
                  />
                  <label htmlFor="check-003">Large Size</label>
                </div>
                <div className="switch-danger swich-size my-3">
                  <input
                    type="checkbox"
                    id="check-004"
                    className="toggle"
                    checked={switch004}
                    onChange={() => handleSwitchChange(setSwitch004)}
                  />
                  <label htmlFor="check-004">Normal Unchecked</label>
                </div>
                <div className="switch-warning swich-size2 my-3">
                  <input
                    type="checkbox"
                    id="check-005"
                    className="toggle"
                    checked={switch005}
                    onChange={() => handleSwitchChange(setSwitch005)}
                  />
                  <label htmlFor="check-005">Small Size</label>
                </div>
                <div className="switch-info swich-size2 my-3">
                  <input
                    type="checkbox"
                    id="check-006"
                    className="toggle"
                    checked={switch006}
                    onChange={() => handleSwitchChange(setSwitch006)}
                  />
                  <label htmlFor="check-006">Small Size Unchecked</label>
                </div>
              </CardBody>
            </Card>
          </Col>

          <Col sm="6" xxl="4">
            <Card>
              <CardHeader>
                <h5>Switch Variants</h5>
              </CardHeader>
              <CardBody className="main-switch main-switch-color switch-unchecked switch_border">
                <div className="switch-primary switch-unchecked-primary switch-border-primary my-3">
                  <input
                    type="checkbox"
                    id="check5"
                    className="toggle m-3"
                    checked={switchStates.check5}
                    onChange={() => handleSwitchChange1("check5")}
                  />
                  <label htmlFor="check5">Primary</label>
                </div>
                <div className="switch-secondary switch-unchecked-secondary switch-border-secondary my-3">
                  <input
                    type="checkbox"
                    id="check6"
                    className="toggle m-3"
                    checked={switchStates.check6}
                    onChange={() => handleSwitchChange1("check6")}
                  />
                  <label htmlFor="check6">Secondary</label>
                </div>
                <div className="switch-success switch-unchecked-success switch-border-success my-3">
                  <input
                    type="checkbox"
                    id="check7"
                    className="toggle m-3"
                    checked={switchStates.check7}
                    onChange={() => handleSwitchChange1("check7")}
                  />
                  <label htmlFor="check7">Success</label>
                </div>
                <div className="switch-danger switch-unchecked-danger switch-border-danger my-3">
                  <input
                    type="checkbox"
                    id="check8"
                    className="toggle m-3"
                    checked={switchStates.check8}
                    onChange={() => handleSwitchChange1("check8")}
                  />
                  <label htmlFor="check8">Info</label>
                </div>
                <div className="switch-warning switch-unchecked-warning switch-border-warning my-3">
                  <input
                    type="checkbox"
                    id="check9"
                    className="toggle m-3"
                    checked={switchStates.check9}
                    onChange={() => handleSwitchChange1("check9")}
                  />
                  <label htmlFor="check9">Warning</label>
                </div>
                <div className="switch-info switch-unchecked-info switch-border-info my-3">
                  <input
                    type="checkbox"
                    id="check10"
                    className="toggle m-3"
                    checked={switchStates.check10}
                    onChange={() => handleSwitchChange1("check10")}
                  />
                  <label htmlFor="check10">Info</label>
                </div>
                <div className="switch-dark switch-unchecked-dark switch-border-dark my-3">
                  <input
                    type="checkbox"
                    id="check01"
                    className="toggle m-3"
                    checked={switchStates.check01}
                    onChange={() => handleSwitchChange1("check01")}
                  />
                  <label htmlFor="check01">Dark</label>
                </div>
              </CardBody>
            </Card>
          </Col>

          <Col sm="6" xxl="4">
            <Card>
              <CardHeader>
                <h5>Switch Outline</h5>
              </CardHeader>
              <CardBody className="switch_border main-switch switch-unchecked">
                <div className="switch-border-primary switch-primary switch-unchecked-primary my-3">
                  <input
                    type="checkbox"
                    id="check-02"
                    className="toggle switch-border-primary"
                    checked={switchStates2.check02}
                    onChange={() => handleSwitchChange2("check02")}
                  />
                  <label htmlFor="check-02">Primary</label>
                </div>
                <div className="switch-border-secondary switch-secondary switch-unchecked-secondary my-3">
                  <input
                    type="checkbox"
                    id="check-03"
                    className="toggle"
                    checked={switchStates2.check03}
                    onChange={() => handleSwitchChange2("check03")}
                  />
                  <label htmlFor="check-03">Secondary</label>
                </div>
                <div className="switch-border-success switch-success switch-unchecked-success my-3">
                  <input
                    type="checkbox"
                    id="check-04"
                    className="toggle switch-border-primary"
                    checked={switchStates2.check04}
                    onChange={() => handleSwitchChange2("check04")}
                  />
                  <label htmlFor="check-04">Success</label>
                </div>
                <div className="switch-border-danger switch-danger switch-unchecked-danger my-3">
                  <input
                    type="checkbox"
                    id="check-05"
                    className="toggle"
                    checked={switchStates2.check05}
                    onChange={() => handleSwitchChange2("check05")}
                  />
                  <label htmlFor="check-05">Danger</label>
                </div>
                <div className="switch-border-warning switch-warning switch-unchecked-warning my-3">
                  <input
                    type="checkbox"
                    id="check-06"
                    className="toggle"
                    checked={switchStates2.check06}
                    onChange={() => handleSwitchChange2("check06")}
                  />
                  <label htmlFor="check-06">Warning</label>
                </div>
                <div className="switch-border-info switch-info switch-unchecked-info my-3">
                  <input
                    type="checkbox"
                    id="check-07"
                    className="toggle"
                    checked={switchStates2.check07}
                    onChange={() => handleSwitchChange2("check07")}
                  />
                  <label htmlFor="check-07">Info</label>
                </div>
                <div className="switch-border-dark switch-dark switch-unchecked-dark my-3">
                  <input
                    type="checkbox"
                    id="check-08"
                    className="toggle"
                    checked={switchStates2.check08}
                    onChange={() => handleSwitchChange2("check08")}
                  />
                  <label htmlFor="check-08">Dark</label>
                </div>
              </CardBody>
            </Card>
          </Col>
        </Row>
      </Container>
    </div>
  );
};

export default SwitchPage;
