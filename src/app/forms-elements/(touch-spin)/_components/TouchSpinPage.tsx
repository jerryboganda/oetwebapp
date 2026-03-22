"use client";

import React, { useState } from "react";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import {
  Button,
  Input,
  Row,
  Col,
  Card,
  CardHeader,
  CardBody,
  Container,
  Dropdown,
  DropdownToggle,
  DropdownMenu,
  DropdownItem,
} from "reactstrap";
import { IconCreditCard } from "@tabler/icons-react";

const TouchSpinPage = () => {
  // Card 1 states
  const [count1A, setCount1A] = useState(25);
  const [count2A, setCount2A] = useState(25);
  const [count3A, setCount3A] = useState(25);

  // Card 2 states
  const [count1B, setCount1B] = useState(25);
  const [count2B, setCount2B] = useState(25);
  const [count3B, setCount3B] = useState(25);

  const [count1C, setCount1C] = useState(25);
  const [count2C, setCount2C] = useState(25);
  const [count3C, setCount3C] = useState(25);

  const [count1D, setCount1D] = useState(25);
  const [count2D, setCount2D] = useState(25);
  const [count3D, setCount3D] = useState(25);

  const [count1G, setCount1G] = useState(25);
  const [count2G, setCount2G] = useState(25);
  const [count3G, setCount3G] = useState(25);

  const [count1E, setCount1E] = useState(25);
  const [count2E, setCount2E] = useState(25);
  const [count3E, setCount3E] = useState(25);
  const [count, setCount] = useState(25);
  const [count1, setCount1] = useState(25);

  const [count2, setCount2] = useState(25);
  const [dropdownOpen, setDropdownOpen] = useState(false);

  const [count3, setCount3] = useState(25);

  const toggleDropdown = () => setDropdownOpen(!dropdownOpen);
  return (
    <div>
      <Container fluid>
        <Breadcrumbs
          mainTitle="Touch spin"
          title="Forms elements"
          path={["Touch spin"]}
          Icon={IconCreditCard}
        />
        <Row className="main-touchspin">
          {/* First Card */}
          <Col xl="3">
            <Card>
              <CardHeader>
                <h5>Basic Touchspin - Card A</h5>
              </CardHeader>
              <CardBody>
                <Row>
                  {[
                    { count: count1A, set: setCount1A, color: "light-primary" },
                    {
                      count: count2A,
                      set: setCount2A,
                      color: "light-secondary",
                    },
                    { count: count3A, set: setCount3A, color: "light-success" },
                  ].map(({ count, set, color }, index) => (
                    <Col key={index} className="mt-2">
                      <div className="simplespin">
                        <Button
                          color={color}
                          onClick={() => set(count - 1)}
                          className="circle-btn decrement"
                        >
                          -
                        </Button>
                        <Input
                          type="text"
                          value={count}
                          readOnly
                          className="app-small-touchspin count"
                        />
                        <Button
                          color={color}
                          onClick={() => set(count + 1)}
                          className="circle-btn increment"
                        >
                          +
                        </Button>
                      </div>
                    </Col>
                  ))}
                </Row>
              </CardBody>
            </Card>
          </Col>
          {/* Second Card */}
          <Col xl="3">
            <Card>
              <CardHeader>
                <h5>Basic Touchspin - Card B</h5>
              </CardHeader>
              <CardBody>
                <Row>
                  {[
                    { count: count1B, set: setCount1B },
                    {
                      count: count2B,
                      set: setCount2B,
                    },
                    { count: count3B, set: setCount3B },
                  ].map(({ count, set }, index) => (
                    <Col key={index} className="mt-2">
                      <div className="simplespin">
                        <a
                          onClick={() => set(count - 1)}
                          className="circle-btn decrement cursor-pointer"
                        >
                          -
                        </a>
                        <Input
                          type="text"
                          value={count}
                          readOnly
                          className="app-small-touchspin count"
                        />
                        <a
                          onClick={() => set(count + 1)}
                          className="circle-btn increment cursor-pointer"
                        >
                          +
                        </a>
                      </div>
                    </Col>
                  ))}
                </Row>
              </CardBody>
            </Card>
          </Col>
          <Col xl="3">
            <Card className="equal-card">
              <CardHeader>
                <h5>Basic Touchspin - Card B</h5>
              </CardHeader>
              <CardBody>
                <Row>
                  {[
                    { count: count1C, set: setCount1C, color: "primary" },
                    {
                      count: count2C,
                      set: setCount2C,
                      color: "secondary",
                    },
                    { count: count3C, set: setCount3C, color: "success" },
                  ].map(({ count, set, color }, index) => (
                    <Col key={index} className="mt-3">
                      <div className="custom-touchspin">
                        <button
                          onClick={() => set(count - 1)}
                          className={`touchspin-circle-btn  btn-${color} text-white decrement`}
                        >
                          -
                        </button>
                        <Input
                          type="text"
                          value={count}
                          readOnly
                          className="app-small-touchspin b-1-primary count"
                        />
                        <button
                          onClick={() => set(count + 1)}
                          className={`touchspin-circle-btn  btn-${color} text-white increment`}
                        >
                          +
                        </button>
                      </div>
                    </Col>
                  ))}
                </Row>
              </CardBody>
            </Card>
          </Col>
          <Col xl="3">
            <Card className="equal-card">
              <CardHeader>
                <h5>Basic Touchspin - Card B</h5>
              </CardHeader>
              <CardBody>
                <Row>
                  {[
                    { count: count1D, set: setCount1D, color: "primary" },
                    {
                      count: count2D,
                      set: setCount2D,
                      color: "secondary",
                    },
                    { count: count3D, set: setCount3D, color: "success" },
                  ].map(({ count, set, color }, index) => (
                    <Col key={index} className="mt-3">
                      <div className="custom-touchspin">
                        <button
                          onClick={() => set(count - 1)}
                          className={`touchspin-circle-btn  btn-${color} text-white decrement`}
                        >
                          -
                        </button>
                        <Input
                          type="text"
                          value={count}
                          readOnly
                          className="app-small-touchspin rounded-pill b-1-primary count"
                        />
                        <button
                          onClick={() => set(count + 1)}
                          className={`touchspin-circle-btn  btn-${color} text-white increment`}
                        >
                          +
                        </button>
                      </div>
                    </Col>
                  ))}
                </Row>
              </CardBody>
            </Card>
          </Col>
          <Col xxl="6" xl="6" lg="12">
            <Card>
              <CardHeader>
                <h5>Custom Round Touchspin</h5>
              </CardHeader>
              <CardBody>
                <Row>
                  {[
                    { count: count1G, set: setCount1G, color: "primary" },
                    {
                      count: count2G,
                      set: setCount2G,
                      color: "secondary",
                    },
                    { count: count3G, set: setCount3G, color: "success" },
                  ].map(({ count, set, color }, index) => (
                    <Col
                      key={index}
                      md="6"
                      lg="4"
                      className="coustom-touchspin-col"
                    >
                      <div className="simplerounded d-flex">
                        <button
                          className={`btn btn-${color} btn-left decrement`}
                          onClick={() => set(count - 1)}
                        >
                          -
                        </button>
                        <Input
                          type="text"
                          className="app-touchspin border-0 count text-center"
                          readOnly
                          value={count}
                        />
                        <button
                          className={`btn btn-${color} btn-right increment`}
                          onClick={() => set(count + 1)}
                        >
                          +
                        </button>
                      </div>
                    </Col>
                  ))}
                </Row>
              </CardBody>
            </Card>
          </Col>
          <Col xxl="6" xl="6" lg="12">
            <Card>
              <CardHeader>
                <h5>Custom Round Touchspin</h5>
              </CardHeader>
              <CardBody>
                <Row>
                  {[
                    { count: count1E, set: setCount1E, color: "primary" },
                    {
                      count: count2E,
                      set: setCount2E,
                      color: "secondary",
                    },
                    { count: count3E, set: setCount3E, color: "success" },
                  ].map(({ count, set, color }, index) => (
                    <Col
                      key={index}
                      md="6"
                      lg="4"
                      className="coustom-touchspin-col"
                    >
                      <div className="simplerounded d-flex">
                        <button
                          className={`btn btn-${color} round decrement`}
                          onClick={() => set(count - 1)}
                        >
                          -
                        </button>
                        <Input
                          type="text"
                          className="app-touchspin border-0 count text-center"
                          readOnly
                          value={count}
                        />
                        <button
                          className={`btn btn-${color} round decrement`}
                          onClick={() => set(count + 1)}
                        >
                          +
                        </button>
                      </div>
                    </Col>
                  ))}
                </Row>
              </CardBody>
            </Card>
          </Col>
          <Col lg="6">
            <Card>
              <CardHeader>
                <h5>Default Touchspin</h5>
              </CardHeader>
              <CardBody>
                <div className="d-flex">
                  <Button
                    color="primary"
                    className="btn-primary b-r-0 decrement"
                    onClick={() => setCount(count - 1)}
                  >
                    -
                  </Button>
                  <Input
                    className="form-control app-touchspin count"
                    type="text"
                    value={count}
                    readOnly
                  />
                  <Button
                    className="btn-secondary b-r-0 increment"
                    onClick={() => setCount(count + 1)}
                  >
                    +
                  </Button>
                </div>
              </CardBody>
            </Card>
          </Col>
          <Col lg="6">
            <Card>
              <CardHeader>
                <h5>Horizontal Touchspin</h5>
              </CardHeader>
              <CardBody>
                <div className="d-flex" id="small-horizontal-touchspin">
                  <Button
                    color="primary"
                    className="btn-primary b-r-0 decrement"
                    onClick={() => setCount1(count1 - 1)}
                  >
                    -
                  </Button>
                  <Input
                    className="form-control app-touchspin count"
                    type="text"
                    value={count1}
                    readOnly
                  />
                  <Button
                    className="btn-secondary b-r-0 increment"
                    onClick={() => setCount1(count1 + 1)}
                  >
                    +
                  </Button>
                </div>
              </CardBody>
            </Card>
          </Col>
          <Col lg="6">
            <Card>
              <CardHeader>
                <h5>Touchspin With Dropdown</h5>
              </CardHeader>
              <CardBody>
                <div
                  className="d-flex touchspin-with-dropdown"
                  id="touchspin-with-dropdown"
                >
                  <Button
                    color="primary"
                    className="btn-primary b-r-0 decrement"
                    onClick={() => setCount2(count2 - 1)}
                  >
                    -
                  </Button>

                  <span className="input-group-text tochspin-pre-class text-light-secondary text-dark b-r-0">
                    Pre
                  </span>
                  <Input
                    className="form-control app-touchspin count"
                    type="text"
                    value={count2}
                    readOnly
                  />

                  <span className="input-group-text tochspin-pre-class text-light-secondary text-dark b-r-0">
                    Post
                  </span>

                  <Button
                    className="btn-secondary b-r-0 increment"
                    onClick={() => setCount2(count2 + 1)}
                  >
                    +
                  </Button>

                  <Dropdown isOpen={dropdownOpen} toggle={toggleDropdown}>
                    <DropdownToggle caret className="btn text-light-secondary">
                      Dropdown
                    </DropdownToggle>
                    <DropdownMenu>
                      <DropdownItem header>Actions</DropdownItem>
                      <DropdownItem>Action</DropdownItem>
                      <DropdownItem>Another action</DropdownItem>
                      <DropdownItem>Something else here</DropdownItem>
                      <DropdownItem divider />
                      <DropdownItem>Separated link</DropdownItem>
                    </DropdownMenu>
                  </Dropdown>
                </div>
              </CardBody>
            </Card>
          </Col>
          <Col lg="6">
            <Card>
              <CardHeader>
                <h5>Touchspin With Postfix & Prefix</h5>
              </CardHeader>
              <CardBody>
                <div className="d-flex touchspin-with-PostPre">
                  <Button
                    className="btn-primary b-r-0 decrement"
                    onClick={() => setCount3(count3 - 1)}
                  >
                    -
                  </Button>

                  <span className="input-group-text b-r-0">#</span>
                  <Input
                    className="form-control app-touchspin count"
                    type="text"
                    value={count3}
                    readOnly
                  />

                  <span className="input-group-text b-r-0">%</span>

                  <Button
                    className="btn-secondary b-r-0 increment"
                    onClick={() => setCount3(count3 + 1)}
                  >
                    +
                  </Button>
                </div>
              </CardBody>
            </Card>
          </Col>
        </Row>
      </Container>
    </div>
  );
};

export default TouchSpinPage;
