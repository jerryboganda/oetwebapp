"use client";
import React, { useEffect, useState } from "react";
import type { ActionMeta } from "react-select";
import {
  Card,
  CardBody,
  CardHeader,
  Col,
  Container,
  Row,
  Label,
} from "reactstrap";
import MultiSelect from "@/app/forms-elements/(select2)/_components/MultiSelect";
import dynamic from "next/dynamic";

const Select = dynamic(() => import("react-select"), {
  ssr: false,
  loading: () => <div>Loading...</div>,
});

const selectOptions = [
  { value: "AL", label: "Alabama" },
  { value: "WY", label: "Wyoming" },
  { value: "WD", label: "Coming" },
  { value: "AF", label: "Hanry Die" },
  { value: "TU", label: "John Doe" },
];

const Select2Page = () => {
  const [isDisable, setIsDisabled] = useState(false);

  const [selectedStates, setSelectedStates] = useState<
    { value: string; label: string }[]
  >([]);
  const [selectedDark, setSelectedDark] = useState<
    { value: string; label: string }[]
  >([]);

  useEffect(() => {
    if (selectOptions.length > 0) {
      setSelectedStates(
        selectOptions.filter((option) => ["AL", "WY"].includes(option.value))
      );
      setSelectedDark(
        selectOptions.filter((option) => ["AL", "WY"].includes(option.value))
      );
    }
  }, []);

  const handleStateChange = (
    selected: unknown,
    _actionMeta: ActionMeta<unknown>
  ) => {
    setSelectedStates(
      Array.isArray(selected)
        ? (selected as { value: string; label: string }[])
        : []
    );
  };
  const handleDarkChange = (
    selected: unknown,
    _actionMeta: ActionMeta<unknown>
  ) => {
    setSelectedDark(
      Array.isArray(selected)
        ? (selected as { value: string; label: string }[])
        : []
    );
  };

  return (
    <Container fluid>
      <Row className=" m-1">
        <Col xs="12">
          <h4 className="main-title">Select2</h4>
          <ul className="app-line-breadcrumbs mb-3">
            <li className="">
              <a href="#" className="f-s-14 f-w-500">
                <span>
                  <i className="ph-duotone  ph-cardholder f-s-16"></i> Forms
                  elements
                </span>
              </a>
            </li>
            <li className="active">
              <a href="#" className="f-s-14 f-w-500">
                Select2
              </a>
            </li>
          </ul>
        </Col>
      </Row>
      <Row>
        <Col xs="12">
          <Card>
            <CardHeader>
              <h5 className="m-0">Select2 With Color Tags</h5>
            </CardHeader>
            <CardBody>
              <Row className="app-form">
                <Col xl="6">
                  <div className="select_primary">
                    <Label className="form-label">Select Primary</Label>
                    <MultiSelect
                      options={selectOptions}
                      placeholder="Select an option"
                      defaultValue={["AL", "WY"]}
                    />
                  </div>
                </Col>
                <Col xl="6">
                  <div className="select_secondary mt-xl-0 mt-4">
                    <Label className="form-label">Select Secondary</Label>
                    <MultiSelect
                      options={selectOptions}
                      placeholder="Select an option"
                      defaultValue={["AL", "WY"]}
                    />
                  </div>
                </Col>
                <Col xl="6">
                  <div className="select_success mt-4">
                    <Label className="form-label">Select Success</Label>
                    <MultiSelect
                      options={selectOptions}
                      placeholder="Select an option"
                      defaultValue={["AL", "WY"]}
                    />
                  </div>
                </Col>
                <Col xl="6">
                  <div className="select_danger mt-4">
                    <Label className="form-label">Select Danger</Label>
                    <MultiSelect
                      options={selectOptions}
                      placeholder="Select an option"
                      defaultValue={["AL", "WY"]}
                    />
                  </div>
                </Col>
                <Col xl="6">
                  <div className="select_warning mt-4">
                    <Label className="form-label">Select Warning</Label>
                    <MultiSelect
                      options={selectOptions}
                      placeholder="Select an option"
                      defaultValue={["AL", "WY"]}
                    />
                  </div>
                </Col>
                <Col xl="6">
                  <div className="select_info mt-4">
                    <Label className="form-label">Select Info</Label>
                    <Select
                      hideSelectedOptions={false}
                      isMulti
                      options={selectOptions}
                      value={selectedStates}
                      onChange={handleStateChange}
                      classNamePrefix="select-basic-multiple-four w-100"
                      className="w-100"
                    />
                  </div>
                </Col>
                <Col xl="6">
                  <div className="select_dark mt-4">
                    <Label className="form-label">Select Dark</Label>
                    <Select
                      hideSelectedOptions={false}
                      isMulti
                      options={selectOptions}
                      value={selectedDark}
                      onChange={handleDarkChange}
                      classNamePrefix="select-basic-multiple-four w-100"
                      className="w-100"
                    />
                  </div>
                </Col>
              </Row>
            </CardBody>
          </Card>
        </Col>

        <Col xs="12">
          <Card>
            <CardHeader>
              <h5>Select2</h5>
            </CardHeader>
            <CardBody>
              <div className="row app-form">
                {/* Basic Select */}
                <div className="col-md-6 col-xl-4">
                  <div className="mt-4">
                    <Label className="form-label">Basic</Label>
                    <Select
                      hideSelectedOptions={false}
                      options={selectOptions}
                      placeholder="Select an option"
                      classNamePrefix="custom-select"
                      className="select-example"
                    />
                  </div>
                </div>

                {/* Multiple Select */}
                <div className="col-md-6 col-xl-4">
                  <div className="mt-4">
                    <Label className="form-label">Multiple</Label>
                    <Select
                      hideSelectedOptions={false}
                      isMulti
                      options={[
                        { value: "orange", label: "Orange" },
                        { value: "purple", label: "Purple" },
                        { value: "white", label: "White" },
                      ]}
                      defaultValue={[
                        { value: "orange", label: "Orange" },
                        { value: "purple", label: "Purple" },
                      ]}
                      placeholder="Select an option"
                      classNamePrefix="custom-select"
                      className="select-1"
                    />
                  </div>
                </div>

                <div className="col-md-6 col-xl-4">
                  <div className="mt-4">
                    <Label className="form-label">Disabled</Label>
                    <Select
                      hideSelectedOptions={false}
                      isMulti
                      options={selectOptions}
                      value={selectedDark}
                      isDisabled
                      onChange={handleDarkChange}
                      classNamePrefix="select-basic-multiple-four w-100"
                      className="w-100"
                    />
                  </div>
                </div>

                {/* Select with Icons */}
                <div className="col-md-6 col-xl-4">
                  <div className="mt-4">
                    <Label className="form-label">Icon</Label>
                    <Select
                      hideSelectedOptions={false}
                      options={[
                        { value: "ti-brand-html5", label: "HTML5" },
                        { value: "ti-brand-codepen", label: "Codepen" },
                        { value: "ti-brand-javascript", label: "JavaScript" },
                        { value: "ti-brand-css3", label: "CSS3" },
                        { value: "ti-brand-bootstrap", label: "Bootstrap 5" },
                      ]}
                      defaultValue={[
                        { value: "ti-brand-html5", label: "HTML5" },
                      ]}
                      placeholder="Select an option"
                      classNamePrefix="custom-select"
                      className="select2-icon"
                    />
                  </div>
                </div>

                {/* RTL Support Select */}
                <div className="col-md-6 col-xl-4">
                  <div className="mt-4">
                    <Label className="form-label">RTL support</Label>
                    <Select
                      hideSelectedOptions={false}
                      isRtl
                      isMulti
                      options={selectOptions}
                      placeholder="Select an option"
                      classNamePrefix="custom-select"
                      className="select-example-rtl w-100"
                    />
                  </div>
                </div>

                {/* Limit Selections Select */}
                <div className="col-md-6 col-xl-4">
                  <div className="mt-4">
                    <Label className="form-label">
                      Limit The Number Of Selections
                    </Label>
                    <Select
                      hideSelectedOptions={false}
                      isMulti
                      options={selectOptions}
                      placeholder="Select an option"
                      classNamePrefix="custom-select"
                      className="select-basic-multiple-four w-100"
                    />
                  </div>
                </div>

                {/* Disable Results Select */}
                <div className="col-md-6 col-xl-4">
                  <div className="mt-4">
                    <Label className="form-label">Disable Results</Label>
                    <Select
                      hideSelectedOptions={false}
                      isMulti
                      options={[
                        { value: "AL", label: "Alabama" },
                        {
                          value: "WY",
                          label: "Wyoming (disabled)",
                          isDisabled: true,
                        },
                        { value: "WY2", label: "Coming" },
                      ]}
                      placeholder="Select an option"
                      classNamePrefix="custom-select"
                      className="select-example-two w-100"
                    />
                  </div>
                </div>

                {/* Flag Selection Select */}
                <div className="col-md-6 col-xl-4">
                  <div className="mt-4">
                    <Label className="form-label">Flags selection</Label>
                    <Select
                      hideSelectedOptions={false}
                      isMulti
                      options={[
                        { value: "flag-icon-ind", label: "🇮🇳 India" },
                        { value: "flag-icon-abw", label: "🇦🇼 Aruba" },
                        { value: "flag-icon-afg", label: "🇦🇫 Afghanistan" },
                        { value: "flag-icon-aia", label: "🇦🇮 Anguilla" },
                        { value: "flag-icon-ala", label: "🇦🇽 Åland Islands" },
                      ]}
                      placeholder="Select an option"
                      classNamePrefix="custom-select"
                      className="select2-icons w-100"
                      formatOptionLabel={(option: unknown) => {
                        const e = option as { value: string; label: string };
                        return (
                          <div className="d-flex align-center g-10">
                            <span className={e.value} /> {e.label}
                          </div>
                        );
                      }}
                    />
                  </div>
                </div>

                {/* Enable/Disable Select */}
                <div className="col-md-6 col-xl-4">
                  <div className="mt-4">
                    <Label className="form-label">Enable-Disable</Label>
                    <Select
                      hideSelectedOptions={false}
                      isDisabled={isDisable}
                      defaultValue={selectOptions[0]}
                      options={selectOptions}
                      placeholder="Select an option"
                      classNamePrefix="custom-select"
                      className="select-basic-multiple-five w-100"
                    />
                  </div>
                  <div className="text-end">
                    <button
                      onClick={() => setIsDisabled(false)}
                      className="btn btn-primary select-basic-multiple-seven mt-3"
                    >
                      Enable
                    </button>{" "}
                    <button
                      onClick={() => setIsDisabled(true)}
                      className="btn btn-secondary select-basic-multiple-six mt-3"
                    >
                      Disable
                    </button>
                  </div>
                </div>
              </div>
            </CardBody>
          </Card>
        </Col>

        {/* Default Select */}
        <Col xs="12">
          <Card>
            <CardHeader>
              <h5>Default Select</h5>
            </CardHeader>
            <CardBody>
              <div className="row main-select">
                <div className="col-md-6 col-xl-4">
                  <form className="app-form">
                    <select
                      className="form-select my-3"
                      aria-label="Default select example"
                      defaultValue="1"
                    >
                      <option>Select your Status</option>
                      <option value="1">Declined Payment</option>
                      <option value="2">Delivery Error</option>
                      <option value="3">Wrong Amount</option>
                    </select>
                  </form>
                </div>
                <div className="col-md-6 col-xl-4">
                  <form className="app-form">
                    <select
                      className="form-select rounded-pill my-3"
                      aria-label="Default select example"
                      defaultValue="1"
                    >
                      <option>Search for services</option>
                      <option value="1">Information Architecture</option>
                      <option value="2">UI/UX Design</option>
                      <option value="3">Back End Development</option>
                    </select>
                  </form>
                </div>
                <div className="col-md-6 col-xl-4">
                  <form className="app-form">
                    <select
                      className="form-select my-3"
                      aria-label="Disabled select example"
                      disabled
                      defaultValue="1"
                    >
                      <option>Open this select menu (Disabled)</option>
                      <option value="1">One</option>
                      <option value="2">Two</option>
                      <option value="3">Three</option>
                    </select>
                  </form>
                </div>
              </div>
            </CardBody>
          </Card>
        </Col>

        {/* Menu Size */}
        <Col xs="12">
          <Card>
            <CardHeader>
              <h5>Menu Size</h5>
            </CardHeader>
            <CardBody>
              <div className="row main-select">
                <div className="col-md-6">
                  <form className="app-form">
                    <select
                      className="form-select"
                      multiple={true}
                      aria-label="multiple select example"
                      defaultValue={["1"]}
                    >
                      <option disabled>
                        Open this select menu (multiple select option)
                      </option>
                      <option value="1">One</option>
                      <option value="2">Two</option>
                      <option value="3">Three</option>
                    </select>
                  </form>
                </div>
                <div className="col-md-6">
                  <form className="app-form">
                    <select
                      className="form-select"
                      size={3}
                      aria-label="size 3 select example"
                      defaultValue="1"
                    >
                      <option>Open this select menu (select menu size)</option>
                      <option value="1">One</option>
                      <option value="2">Two</option>
                      <option value="3">Three</option>
                      <option value="4">Four</option>
                      <option value="5">Five</option>
                    </select>
                  </form>
                </div>
              </div>
            </CardBody>
          </Card>
        </Col>

        {/* Select Size */}
        <Col xs="12">
          <Card>
            <CardHeader>
              <h5>Select Size</h5>
            </CardHeader>
            <CardBody>
              <div className="row app-form">
                <div className="col-md-6 col-xl-4 mb-3">
                  <select
                    className="form-select form-select-sm"
                    aria-label=".form-select-sm example"
                    defaultValue="1"
                  >
                    <option>Open this select menu</option>
                    <option value="1">One</option>
                    <option value="2">Two</option>
                    <option value="3">Three</option>
                  </select>
                </div>
                <div className="col-md-6 col-xl-4 mb-3">
                  <select
                    className="form-select"
                    aria-label=".form-select-sm example"
                    defaultValue="1"
                  >
                    <option>Open this select menu</option>
                    <option value="1">One</option>
                    <option value="2">Two</option>
                    <option value="3">Three</option>
                  </select>
                </div>
                <div className="col-md-6 col-xl-4 mb-3">
                  <select
                    className="form-select form-select-lg"
                    aria-label=".form-select-lg example"
                    defaultValue="1"
                  >
                    <option>Open this select menu</option>
                    <option value="1">One</option>
                    <option value="2">Two</option>
                    <option value="3">Three</option>
                  </select>
                </div>
              </div>
            </CardBody>
          </Card>
        </Col>
      </Row>
    </Container>
  );
};

export default Select2Page;
