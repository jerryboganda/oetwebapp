"use client";
import React from "react";
import {
  Button,
  Card,
  CardBody,
  CardHeader,
  Col,
  Container,
  Row,
} from "reactstrap";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import Nouislider from "nouislider-react";
import "nouislider/distribute/nouislider.css";
import { IconCreditCard } from "@tabler/icons-react";

const arbitraryValuesForSlider = [
  "MB",
  "256MB",
  "1GB",
  "8GB",
  "16GB",
  "32GB",
  "GB",
];
const valuesForSlider = [
  1, 2, 3, 4, 5, 6, 7, 8, 10, 12, 14, 16, 20, 24, 28, 32,
];
const format = {
  to: (value: number): string => {
    const index = Math.round(value);
    return (
      index >= 0 && index < arbitraryValuesForSlider.length
        ? arbitraryValuesForSlider[index]
        : ""
    ) as string;
  },
  from: (value: string | number): number =>
    Math.max(0, arbitraryValuesForSlider.indexOf(String(value))),
};

const formatValue = {
  to: (value: number): number => {
    const index = Math.round(value);
    return index >= 0 && index < valuesForSlider.length
      ? valuesForSlider[index]!
      : 0;
  },
  from: (value: string | number): number => {
    const numValue = Number(value);
    return Math.max(0, valuesForSlider.indexOf(numValue));
  },
};

const RangeSliderPage = () => {
  const sliderRef = React.useRef<HTMLDivElement>(null);

  const startPositions = [20, 32, 50, 70, 80, 90];
  const connectConfig = [false, true, true, true, true, false];
  const [slider1, setSlider1] = React.useState(0);
  const [slider2, setSlider2] = React.useState(0);
  const [selectValue, setSelectValue] = React.useState<number>(10);
  const [numberValue, setNumberValue] = React.useState<number>(30);
  const [rgbValues, setRgbValues] = React.useState<number[]>([127, 127, 127]);

  const handleUpdate = (values: (string | number)[], handle: number) => {
    const value = parseFloat(String(values[handle] || ""));
    if (handle === 0) {
      setSelectValue(Math.round(value));
    } else {
      setNumberValue(value);
    }
  };

  const handleSelectChange = (event: React.ChangeEvent<HTMLSelectElement>) => {
    const newValue = parseFloat(event.target.value);
    setNumberValue(isNaN(newValue) ? 0 : newValue);
  };

  const handleNumberChange = (event: React.ChangeEvent<HTMLInputElement>) => {
    const newValue = parseFloat(event.target.value);
    setNumberValue(isNaN(newValue) ? 0 : newValue);
  };

  return (
    <div>
      <Container fluid>
        <Breadcrumbs
          mainTitle="Range Slider"
          title="Forms elements"
          path={["Range slider"]}
          Icon={IconCreditCard}
        />
        <Col>
          <Col className="container-fluid">
            <Col className="row">
              <Col md="12" xl="6">
                <Card>
                  <CardHeader>
                    <h5>Bootstrap Range Sliders</h5>
                  </CardHeader>
                  <CardBody>
                    <Row>
                      <Col md="12">
                        <label htmlFor="customRange1" className="form-label">
                          Basic range Slider
                        </label>
                        <input
                          type="range"
                          className="form-range"
                          id="customRange1"
                        />
                      </Col>
                      <Col md="12">
                        <label htmlFor="disabledRange" className="form-label">
                          Disabled range Slider
                        </label>
                        <input
                          type="range"
                          className="form-range"
                          id="disabledRange"
                          disabled
                        />
                      </Col>
                      <Col md="12">
                        <label htmlFor="customRange2" className="form-label">
                          Min and max range Slider
                        </label>
                        <input
                          type="range"
                          className="form-range"
                          min="0"
                          max="2"
                          id="customRange2"
                        />
                      </Col>
                      <Col md="12">
                        <label htmlFor="customRange3" className="form-label">
                          Steps range Slider
                        </label>
                        <input
                          type="range"
                          className="form-range"
                          min="0"
                          max="5"
                          id="customRange3"
                        />
                      </Col>
                    </Row>
                  </CardBody>
                </Card>
              </Col>
              <Col md="12" xl="6">
                <Card>
                  <CardHeader>
                    <h5>colour variant</h5>
                  </CardHeader>
                  <CardBody className="card-body">
                    <Row>
                      <Col md="6">
                        <div className="mb-3">
                          <label className="form-label">
                            primary range slider
                          </label>
                          <div className="w-full">
                            <Nouislider
                              direction="ltr"
                              orientation="horizontal"
                              range={{ min: 0, max: 100 }}
                              start={[40]}
                              connect={[true, false]}
                              className="slider-round"
                            />
                          </div>
                        </div>
                      </Col>

                      <div className="col-md-6">
                        <div className="mb-3">
                          <label className="form-label">
                            secondary range slider
                          </label>
                          <Nouislider
                            direction="ltr"
                            orientation="horizontal"
                            range={{ min: 0, max: 100 }}
                            start={[40]}
                            connect={[true, false]}
                            className="slider-round slider-secondary"
                          />
                        </div>
                      </div>

                      <div className="col-md-6">
                        <div className="mb-3">
                          <label className="form-label">
                            Success range slider
                          </label>
                          <Nouislider
                            direction="ltr"
                            orientation="horizontal"
                            range={{ min: 0, max: 100 }}
                            start={[40]}
                            connect={[true, false]}
                            className="slider-round slider-success"
                          />
                        </div>
                      </div>

                      <div className="col-md-6">
                        <div className="mb-3">
                          <label className="form-label">
                            danger range slider
                          </label>
                          <Nouislider
                            direction="ltr"
                            orientation="horizontal"
                            range={{ min: 0, max: 100 }}
                            start={[40]}
                            connect={[true, false]}
                            className="slider-round slider-danger"
                          />
                        </div>
                      </div>

                      <div className="col-md-6">
                        <div className="mb-3">
                          <label className="form-label">
                            warning range slider
                          </label>
                          <Nouislider
                            direction="ltr"
                            orientation="horizontal"
                            range={{ min: 0, max: 100 }}
                            start={[40]}
                            connect={[true, false]}
                            className="slider-round slider-warning"
                          />
                        </div>
                      </div>

                      <div className="col-md-6">
                        <div className="mb-3">
                          <label className="form-label">
                            info range slider
                          </label>
                          <Nouislider
                            direction="ltr"
                            orientation="horizontal"
                            range={{ min: 0, max: 100 }}
                            start={[40]}
                            connect={[true, false]}
                            className="slider-round slider-info"
                          />
                        </div>
                      </div>

                      <div className="col-md-6">
                        <div className="mb-3">
                          <label className="form-label">
                            light range slider
                          </label>
                          <Nouislider
                            direction="ltr"
                            orientation="horizontal"
                            range={{ min: 0, max: 100 }}
                            start={[40]}
                            connect={[true, false]}
                            className="slider-round slider-light"
                          />
                        </div>
                      </div>

                      <div className="col-md-6">
                        <div className="mb-3">
                          <label className="form-label">
                            dark range slider
                          </label>
                          <Nouislider
                            direction="ltr"
                            orientation="horizontal"
                            range={{ min: 0, max: 100 }}
                            start={[40]}
                            connect={[true, false]}
                            className="slider-round slider-dark"
                          />
                        </div>
                      </div>
                    </Row>
                  </CardBody>
                </Card>
              </Col>
              <Col xs="12">
                <Card>
                  <CardHeader>
                    <h5>value slider</h5>
                  </CardHeader>
                  <CardBody>
                    <Col xs="12">
                      <div className="mb-4">
                        <label className="form-label f-s-16 text-secondary mb-3">
                          Locking sliders together
                        </label>
                        <Nouislider
                          direction="ltr"
                          orientation="horizontal"
                          range={{ min: 0, max: 100 }}
                          start={20}
                          onUpdate={(
                            values: (string | number)[],
                            handle: number
                          ) => setSlider1(Number(values[handle]))}
                          connect={[true, false]}
                          className="slider-round"
                        />
                        value:
                        <span>{slider1}</span>
                      </div>
                      <div className="mb-5">
                        <Nouislider
                          direction="ltr"
                          orientation="horizontal"
                          range={{ min: 0, max: 100 }}
                          start={20}
                          onUpdate={(
                            values: (string | number)[],
                            handle: number
                          ) => setSlider2(Number(values[handle]))}
                          connect={[true, false]}
                          className="slider-round"
                        />
                        value:
                        <span>{slider2}</span>
                        <div>
                          <Button
                            type="button"
                            color="primary"
                            className="float-end"
                            id="lockbtn"
                          >
                            Lock
                          </Button>
                        </div>
                      </div>
                    </Col>
                    <div className="col-12">
                      <div className="mb-4">
                        <label className="form-label text-secondary f-s-16 mb-3">
                          Multi range slider
                        </label>
                        <Nouislider
                          direction="ltr"
                          orientation="horizontal"
                          range={{
                            min: 0,
                            max: arbitraryValuesForSlider.length - 1,
                          }}
                          start={[2, 4]} // Indices for ["1GB", "16GB"] in arbitraryValuesForSlider
                          connect={[false, true, false]}
                          step={1}
                          format={format}
                          pips={{
                            mode: "steps",
                            density: 50,
                            format: format,
                          }}
                          className="slider-round"
                        />
                      </div>
                    </div>
                    <div className="col-12">
                      <div className="mb-5 mg-t-70">
                        <label className="form-label f-s-16 mb-3 text-secondary">
                          Soft limits
                        </label>
                        <Nouislider
                          direction="ltr"
                          orientation="horizontal"
                          range={{ min: 0, max: 100 }}
                          start={[50]}
                          connect={[true, false]}
                          step={1}
                          pips={{
                            mode: "values",
                            values: [40, 80],
                            density: 2,
                          }}
                          className="colored-slider slider-round"
                        />
                      </div>
                    </div>
                  </CardBody>
                </Card>
              </Col>
              <Col xs="12">
                <Card>
                  <CardHeader>
                    <h5>Tooltip slider</h5>
                  </CardHeader>
                  <CardBody>
                    <Col xs="12">
                      <div className="mb-5">
                        <label className="form-label f-s-16 text-secondary mb-5">
                          values slider
                        </label>
                        <Nouislider
                          direction="ltr"
                          orientation="horizontal"
                          range={{ min: 0, max: valuesForSlider.length - 1 }}
                          start={[5]}
                          connect={[true, false]}
                          step={2}
                          tooltips
                          format={formatValue}
                          pips={{ mode: "steps", format: formatValue }}
                          className="slider-round primary-slider-round"
                        />
                      </div>
                    </Col>

                    <div className="col-12">
                      <div className="mb-5">
                        <label className="form-label f-s-16 text-secondary mb-5">
                          tooltip slider
                        </label>
                        <Nouislider
                          direction="ltr"
                          orientation="horizontal"
                          range={{ min: 0, max: 100 }}
                          start={[20]}
                          connect={[true, false]}
                          tooltips
                          className="slider-round hide-tooltips primary-slider-round"
                        />
                      </div>
                    </div>

                    <div className="col-12">
                      <div className="mb-5" ref={sliderRef}>
                        <label className="form-label f-s-16 text-secondary mb-5">
                          Colored Connect Slider
                        </label>
                        <Nouislider
                          range={{ min: 0, max: 100 }}
                          start={startPositions}
                          connect={true}
                          behaviour="drag"
                          tooltips={connectConfig}
                        />
                      </div>
                    </div>
                  </CardBody>
                </Card>
              </Col>

              <Col xs="12">
                <div className="card">
                  <div className="card-header">
                    <h5>dynamic slider</h5>
                  </div>
                  <div className="card-body">
                    <div className="col-12">
                      <div className="mb-3">
                        <label className="form-label">HTML5 input</label>
                        <Nouislider
                          start={[selectValue || 0, numberValue || 0]}
                          connect
                          range={{ min: -20, max: 40 }}
                          onUpdate={handleUpdate}
                          className="slider-round"
                        />
                      </div>
                      <div className="d-flex gap-2 mb-5">
                        <select
                          id="select-input"
                          className="form-select"
                          value={selectValue}
                          onChange={handleSelectChange}
                        >
                          {Array.from({ length: 61 }, (_, i) => i - 20).map(
                            (num) => (
                              <option key={num} value={num}>
                                {num}
                              </option>
                            )
                          )}
                        </select>
                        <input
                          type="number"
                          id="number-input"
                          className="form-control"
                          value={numberValue}
                          onChange={handleNumberChange}
                        />
                      </div>
                    </div>
                  </div>
                </div>
              </Col>
              <Col xs="12">
                <Row>
                  <Col md={4}>
                    <Card>
                      <CardHeader>
                        <h5>Color picker slider</h5>
                      </CardHeader>
                      <CardBody>
                        <div className="colorpicker-slider">
                          <Nouislider
                            start={127}
                            connect={[true, false]}
                            orientation="vertical"
                            range={{ min: 0, max: 255 }}
                            className="vertical verticalsliders red me-2"
                            onUpdate={(values, handle) => {
                              setRgbValues((prevValues) => {
                                const updatedValues = [...prevValues];
                                updatedValues[0] = Number(values[handle]);
                                return updatedValues;
                              });
                            }}
                          />
                          <Nouislider
                            start={127}
                            connect={[true, false]}
                            orientation="vertical"
                            range={{ min: 0, max: 255 }}
                            className="vertical verticalsliders green me-2"
                            onUpdate={(values, handle) => {
                              setRgbValues((prevValues) => {
                                const updatedValues = [...prevValues];
                                updatedValues[1] = Number(values[handle]);
                                return updatedValues;
                              });
                            }}
                          />
                          <Nouislider
                            start={127}
                            connect={[true, false]}
                            orientation="vertical"
                            range={{ min: 0, max: 255 }}
                            className="vertical verticalsliders blue"
                            onUpdate={(values, handle) => {
                              setRgbValues((prevValues) => {
                                const updatedValues = [...prevValues];
                                updatedValues[2] = Number(values[handle]);
                                return updatedValues;
                              });
                            }}
                          />
                          <div
                            className="result"
                            id="result"
                            style={{
                              backgroundColor:
                                "rgb(" + rgbValues.join(",") + ")",
                            }}
                          />
                        </div>
                      </CardBody>
                    </Card>
                  </Col>
                  <Col md={4}>
                    <Card>
                      <CardHeader>
                        <h5>Vertical slider</h5>
                      </CardHeader>
                      <CardBody>
                        <Nouislider
                          start={20}
                          connect={[true, false]}
                          orientation="vertical"
                          range={{ min: 0, max: 100 }}
                          className="vertical m-auto"
                        />
                      </CardBody>
                    </Card>
                  </Col>
                  <Col md={4}>
                    <Card>
                      <CardHeader>
                        <h5>Toggle slider</h5>
                      </CardHeader>
                      <CardBody>
                        <Nouislider
                          start={0}
                          orientation="vertical"
                          range={{ min: [0, 1], max: 1 }}
                          className="vertical m-auto"
                        />
                      </CardBody>
                    </Card>
                  </Col>
                </Row>
              </Col>
            </Col>
          </Col>
        </Col>
      </Container>
    </div>
  );
};

export default RangeSliderPage;
