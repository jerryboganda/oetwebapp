"use client";
import React, { useState } from "react";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import {
  Card,
  CardHeader,
  CardBody,
  Row,
  Col,
  FormGroup,
  Label,
  Input,
  Container,
} from "reactstrap";
import {
  checkboxData,
  managers,
  paymentMethods,
  radioData,
  services,
  toggleData,
} from "@/Data/FormElements/Checkbox/checkboxData";
import {
  IconBrandFacebook,
  IconBrandInstagram,
  IconBrandSnapchat,
  IconBrandTwitter,
  IconCreditCard,
} from "@tabler/icons-react";

type Platforms = {
  instagram: boolean;
  twitter: boolean;
  facebook: boolean;
  snapchat: boolean;
};

type SelectedImages = {
  [key: string]: boolean;
};

type ToggleOption = {
  id: string;
  label: string;
  className: string;
  checked: boolean;
  disabled?: boolean;
  type?: string;
};

type ToggleGroup = {
  type: string;
  name: string;
  options: ToggleOption[];
};

const CheckboxRadioPage = () => {
  const [selectedPlatforms, setSelectedPlatforms] = useState<Platforms>({
    instagram: true,
    twitter: false,
    facebook: false,
    snapchat: false,
  });

  const handleCheckboxChange = (platform: keyof Platforms) => {
    setSelectedPlatforms((prevState) => ({
      ...prevState,
      [platform]: !prevState[platform],
    }));
  };

  const [selectedOption, setSelectedOption] = useState<string>("instagram");

  const handleRadioChange = (event: React.ChangeEvent<HTMLInputElement>) => {
    setSelectedOption(event.target.value);
  };

  const [selectedImage, setSelectedImage] = useState<string>("3");

  const [selectedImages, setSelectedImages] = useState<SelectedImages>({
    "1": false,
    "2": true,
    "3": true,
  });

  const handleImageSelect = (event: React.ChangeEvent<HTMLInputElement>) => {
    setSelectedImage(event.target.value);
  };

  const handleImgCheckboxChange = (
    event: React.ChangeEvent<HTMLInputElement>
  ) => {
    const value = event.target.value;
    setSelectedImages((prevState) => ({
      ...prevState,
      [value]: !prevState[value],
    }));
  };

  const [selectedPayment, setSelectedPayment] = useState<string | null>(null);
  const [selectedManagers, setSelectedManagers] = useState<string[]>([]);
  const [selectedService, setSelectedService] = useState<string | null>(null);

  const handlePaymentChange = (event: React.ChangeEvent<HTMLInputElement>) => {
    setSelectedPayment(event.target.value);
  };

  const handleManagerChange = (value: string) => {
    setSelectedManagers((prev) =>
      prev.includes(value) ? prev.filter((v) => v !== value) : [...prev, value]
    );
  };

  const handleServiceChange = (event: React.ChangeEvent<HTMLInputElement>) => {
    setSelectedService(event.target.value);
  };
  return (
    <div>
      <Container fluid>
        <Breadcrumbs
          mainTitle="Checkbox & Radio"
          title="Forms elements"
          path={["Checkbox & Radio"]}
          Icon={IconCreditCard}
        />
        <Row>
          <Col md="6">
            <Card>
              <CardHeader>
                <h5>Custom Radio</h5>
              </CardHeader>
              <CardBody>
                <div className="d-flex flex-column gap-2">
                  <FormGroup check className="d-flex align-items-center gap-1">
                    <Input
                      className="form-check-input f-s-18 mb-1 m-1"
                      type="radio"
                      name="flexRadioDefault"
                      id="radio_default1"
                    />
                    <Label
                      check
                      for="radio_default1"
                      className="form-check-label"
                    >
                      Default
                    </Label>
                  </FormGroup>

                  <FormGroup check className="d-flex align-items-center gap-1">
                    <Input
                      className="form-check-input f-s-18 mb-1 m-1"
                      type="radio"
                      name="flexRadioDisabled"
                      id="radio_disabled"
                      disabled
                    />
                    <Label
                      check
                      for="radio_disabled"
                      className="form-check-label"
                    >
                      Disabled
                    </Label>
                  </FormGroup>

                  <FormGroup check className="d-flex align-items-center gap-1">
                    <Input
                      className="form-check-input f-s-18 mb-1 m-1"
                      type="radio"
                      name="flexRadioDefault"
                      id="radio_default2"
                      defaultChecked
                    />
                    <Label
                      check
                      for="radio_default2"
                      className="form-check-label"
                    >
                      Checked
                    </Label>
                  </FormGroup>
                </div>
              </CardBody>
            </Card>
          </Col>

          <Col md="6">
            <Card>
              <CardHeader>
                <h5>Custom Checkbox</h5>
              </CardHeader>
              <CardBody>
                <Row className="d-flex flex-wrap gap-2">
                  <FormGroup check className="d-flex align-items-center gap-1">
                    <Input
                      type="checkbox"
                      id="flexCheck"
                      label="checkbox"
                      className="mg-2"
                      defaultChecked
                    />
                    <Label check for="flexCheck" className="form-check-label">
                      checkbox
                    </Label>
                  </FormGroup>

                  <FormGroup check className="d-flex align-items-center gap-1">
                    <Input
                      type="checkbox"
                      id="flexCheckIndeterminate"
                      label="indeterminate checkbox"
                      className="mg-2"
                    />
                    <Label
                      check
                      for="flexCheckIndeterminate"
                      className="form-check-label"
                    >
                      indeterminate checkbox
                    </Label>
                  </FormGroup>

                  <FormGroup check className="d-flex align-items-center gap-1">
                    <Input
                      type="checkbox"
                      id="flexCheckDisabled"
                      label="Disabled checkbox"
                      disabled
                      className="mg-2"
                    />
                    <Label
                      check
                      for="flexCheckDisabled"
                      className="form-check-label"
                    >
                      Disabled checkbox
                    </Label>
                  </FormGroup>
                </Row>
              </CardBody>
            </Card>
          </Col>

          <Col md="6">
            <Row>
              <Card>
                <CardHeader>
                  <h5>Radio with States</h5>
                </CardHeader>
                <CardBody>
                  <Row>
                    {radioData.map((group, idx) => (
                      <Col key={idx} md={6} xl={4}>
                        <div className="check-container">
                          {group.radios.map((radio) => (
                            <label key={radio.id} className="check-box">
                              <input
                                type="radio"
                                name={group.name}
                                id={radio.id}
                              />
                              <span
                                className={`radiomark ${radio.colorClass} ms-2`}
                              ></span>
                              <span className={radio.textClass}>
                                {radio.label}
                              </span>
                            </label>
                          ))}
                        </div>
                      </Col>
                    ))}
                  </Row>
                </CardBody>
              </Card>
            </Row>
          </Col>

          <Col md="6">
            <Card>
              <CardHeader>
                <h5>Checkbox with States</h5>
              </CardHeader>
              <CardBody>
                <Row>
                  {checkboxData.map((group, idx) => (
                    <Col key={idx} md={6} xl={4}>
                      <div className="check-container">
                        {group.checkboxes.map((checkbox) => (
                          <label key={checkbox.id} className="check-box">
                            <input
                              type="checkbox"
                              name={group.name}
                              id={checkbox.id}
                            />
                            <span
                              className={`checkmark ${checkbox.colorClass} ms-2`}
                            ></span>
                            <span className={checkbox.textClass}>
                              {checkbox.label}
                            </span>
                          </label>
                        ))}
                      </div>
                    </Col>
                  ))}
                </Row>
              </CardBody>
            </Card>
          </Col>

          <Col xs="12">
            <Row>
              {toggleData.map((group: ToggleGroup, idx) => (
                <Col key={idx} md="6" xl="4">
                  <Card className="equal-card">
                    <CardHeader>
                      <h5>
                        {group.type === "radio"
                          ? "Radio Toggle Buttons"
                          : "Checkbox Toggle Buttons"}
                      </h5>
                    </CardHeader>
                    <CardBody>
                      <div className="d-flex flex-wrap gap-2">
                        {group.options.map((option) => (
                          <React.Fragment key={option.id}>
                            <input
                              type={option.type ?? group.type}
                              className="btn-check"
                              id={option.id}
                              name={group.name}
                              defaultChecked={option.checked}
                              disabled={option.disabled || false}
                            />
                            <label
                              className={`btn ${option.className} b-r-22`}
                              htmlFor={option.id}
                            >
                              {option.label}
                            </label>
                          </React.Fragment>
                        ))}
                      </div>
                    </CardBody>
                  </Card>
                </Col>
              ))}
            </Row>
          </Col>

          <Col md="6">
            <Card>
              <CardHeader>
                <h5>Select group Checkbox With Icons</h5>
              </CardHeader>
              <CardBody>
                <div className="form-selectgroup ">
                  <div className="d-flex gap-2 flex-wrap">
                    <Label
                      check
                      className="d-flex align-items-center select-items"
                    >
                      <Input
                        type="checkbox"
                        checked={selectedPlatforms.instagram}
                        onChange={() => handleCheckboxChange("instagram")}
                        className="me-2 select-input"
                      />
                      <span className="select-box">
                        <span className="selectitem">
                          <IconBrandInstagram size={18} /> Instagram
                        </span>
                      </span>
                    </Label>

                    <Label
                      check
                      className="d-flex align-items-center select-items"
                    >
                      <Input
                        type="checkbox"
                        checked={selectedPlatforms.twitter}
                        onChange={() => handleCheckboxChange("twitter")}
                        className="me-2 select-input"
                      />
                      <span className="select-box">
                        <span className="selectitem">
                          <IconBrandTwitter size={18} /> Twitter
                        </span>
                      </span>
                    </Label>

                    <Label
                      check
                      className="d-flex align-items-center select-items"
                    >
                      <Input
                        type="checkbox"
                        checked={selectedPlatforms.facebook}
                        onChange={() => handleCheckboxChange("facebook")}
                        className="me-2 select-input"
                      />
                      <span className="select-box">
                        <span className="selectitem">
                          <IconBrandFacebook size={18} /> Facebook
                        </span>
                      </span>
                    </Label>

                    <Label
                      check
                      className="d-flex align-items-center select-items"
                    >
                      <Input
                        type="checkbox"
                        checked={selectedPlatforms.snapchat}
                        onChange={() => handleCheckboxChange("snapchat")}
                        className="me-2 select-input"
                      />
                      <span className="select-box">
                        <span className="selectitem">
                          <IconBrandSnapchat size={18} /> Snapchat
                        </span>
                      </span>
                    </Label>
                  </div>
                </div>
              </CardBody>
            </Card>
          </Col>

          <Col md="6">
            <Card>
              <CardHeader>
                <h5>Select group Radio With Icons</h5>
              </CardHeader>

              <CardBody>
                <div className="form-selectgroup">
                  <div className="d-flex gap-2 flex-wrap">
                    <Label
                      check
                      className="d-flex align-items-center select-items"
                    >
                      <Input
                        type="radio"
                        name="select-options"
                        value="instagram"
                        checked={selectedOption === "instagram"}
                        onChange={handleRadioChange}
                        className="me-2 select-input"
                      />
                      <span className="select-box">
                        <span className="selectitem">
                          <IconBrandInstagram size={18} /> Instagram
                        </span>
                      </span>
                    </Label>

                    <Label
                      check
                      className="d-flex align-items-center select-items"
                    >
                      <Input
                        type="radio"
                        name="select-options"
                        value="twitter"
                        checked={selectedOption === "twitter"}
                        onChange={handleRadioChange}
                        className="me-2 select-input"
                      />
                      <span className="select-box">
                        <span className="selectitem">
                          <IconBrandTwitter size={18} /> Twitter
                        </span>
                      </span>
                    </Label>

                    <Label
                      check
                      className="d-flex align-items-center select-items"
                    >
                      <Input
                        type="radio"
                        name="select-options"
                        value="facebook"
                        checked={selectedOption === "facebook"}
                        onChange={handleRadioChange}
                        className="me-2 select-input"
                      />
                      <span className="select-box">
                        <span className="selectitem">
                          <IconBrandFacebook size={18} /> Facebook
                        </span>
                      </span>
                    </Label>

                    <Label
                      check
                      className="d-flex align-items-center select-items"
                    >
                      <Input
                        type="radio"
                        name="select-options"
                        value="snapchat"
                        checked={selectedOption === "snapchat"}
                        onChange={handleRadioChange}
                        className="me-2 select-input"
                      />
                      <span className="select-box">
                        <span className="selectitem">
                          <IconBrandSnapchat size={18} /> Snapchat
                        </span>
                      </span>
                    </Label>
                  </div>
                </div>
              </CardBody>
            </Card>
          </Col>

          <Col md="6">
            <Card>
              <CardHeader>
                <h5>Images With Radio</h5>
              </CardHeader>

              <CardBody>
                <FormGroup className="row">
                  <div className="col-sm-6 col-xl-4">
                    <Label className="form-checkimage">
                      <Input
                        type="radio"
                        name="radioimage"
                        value="1"
                        checked={selectedImage === "1"}
                        onChange={handleImageSelect}
                        className="checkimage-input"
                      />
                      <span className="check-box radiobox">
                        <img
                          src="/images/bootstrapslider/02.jpg"
                          className="checkbox-image w-100"
                          alt="Option 1"
                        />
                      </span>
                    </Label>
                  </div>

                  <div className="col-sm-6 col-xl-4">
                    <Label className="form-checkimage">
                      <Input
                        type="radio"
                        name="radioimage"
                        value="2"
                        checked={selectedImage === "2"}
                        onChange={handleImageSelect}
                        className="checkimage-input"
                      />
                      <span className="check-box radiobox">
                        <img
                          src="/images/bootstrapslider/04.jpg"
                          className="checkbox-image w-100"
                          alt="Option 2"
                        />
                      </span>
                    </Label>
                  </div>

                  <div className="col-sm-6 col-xl-4">
                    <Label className="form-checkimage">
                      <Input
                        type="radio"
                        name="radioimage"
                        value="3"
                        checked={selectedImage === "3"}
                        disabled
                        className="checkimage-input"
                      />
                      <span className="check-box radiobox">
                        <img
                          src="/images/bootstrapslider/05.jpg"
                          className="checkbox-image w-100"
                          alt="Option 3"
                        />
                      </span>
                    </Label>
                  </div>
                </FormGroup>
              </CardBody>
            </Card>
          </Col>

          <Col md="6">
            <Card>
              <CardHeader>
                <h5>Images with checkbox</h5>
              </CardHeader>

              <CardBody>
                <FormGroup className="row">
                  <div className="col-md-6 col-xl-4">
                    <Label className="form-checkimage">
                      <Input
                        type="checkbox"
                        value="1"
                        checked={selectedImages[1]}
                        onChange={handleImgCheckboxChange}
                        className="checkimage-input"
                      />
                      <span className="check-box">
                        <img
                          src="/images/bootstrapslider/01.jpg"
                          className="checkbox-image w-100"
                          alt="Option 1"
                        />
                      </span>
                    </Label>
                  </div>

                  <div className="col-md-6 col-xl-4">
                    <Label className="form-checkimage">
                      <Input
                        type="checkbox"
                        value="2"
                        checked={selectedImages[2]}
                        onChange={handleImgCheckboxChange}
                        className="checkimage-input"
                      />
                      <span className="check-box">
                        <img
                          src="/images/bootstrapslider/08.jpg"
                          className="checkbox-image w-100"
                          alt="Option 2"
                        />
                      </span>
                    </Label>
                  </div>

                  <div className="col-md-6 col-xl-4">
                    <Label className="form-checkimage">
                      <Input
                        type="checkbox"
                        value="3"
                        checked={selectedImages[3]}
                        disabled
                        className="checkimage-input"
                      />
                      <span className="check-box">
                        <img
                          src="/images/bootstrapslider/03.jpg"
                          className="checkbox-image w-100"
                          alt="Option 3"
                        />
                      </span>
                    </Label>
                  </div>
                </FormGroup>
              </CardBody>
            </Card>
          </Col>

          <Col md="6" xl="4">
            <Card>
              <CardHeader>
                <h5>Payment Methods</h5>
              </CardHeader>
              <CardBody>
                <FormGroup className="form-selectgroup">
                  {paymentMethods.map((method, index) => (
                    <div key={index} className="select-item">
                      <Input
                        type="radio"
                        name="paymentMethod"
                        id={`payment${index}`}
                        value={method}
                        checked={selectedPayment === method}
                        onChange={handlePaymentChange}
                        className="form-check-input"
                      />
                      <Label
                        for={`payment${index}`}
                        className="form-check-label"
                      >
                        <span className="d-flex align-items-center">
                          <img
                            src={`/images/checkbox-radio/logo${index + 1}.png`}
                            className="w-30 h-30 b-r-16"
                            alt=""
                          />
                          <span className="ms-2">
                            <span className="fs-6">{method}</span>
                            <span className="d-block text-secondary">
                              Select {method} payment method
                            </span>
                          </span>
                        </span>
                      </Label>
                    </div>
                  ))}
                </FormGroup>
              </CardBody>
            </Card>
          </Col>

          <Col md="6" xl="4">
            <Card>
              <CardHeader>
                <h5>Project Manager</h5>
              </CardHeader>
              <CardBody>
                <FormGroup className="form-selectgroup">
                  {managers.map((manager, index) => (
                    <div key={index} className="select-item">
                      <Input
                        type="checkbox"
                        id={`manager${index}`}
                        value={manager.name}
                        checked={selectedManagers.includes(manager.name)}
                        onChange={() => handleManagerChange(manager.name)}
                        className="form-check-input"
                      />
                      <Label
                        for={`manager${index}`}
                        className="form-check-label"
                      >
                        <span className="d-flex align-items-center">
                          <span className="bg-secondary h-30 w-30 d-flex-center b-r-50 position-relative">
                            <img
                              src={`/images/avatar/${manager.img}`}
                              alt=""
                              className="img-fluid b-r-50"
                            />
                            <span className="position-absolute top-0 end-0 p-1 bg-success border border-light rounded-circle"></span>
                          </span>
                          <span className="ms-2">
                            <span className="fs-6">{manager.name}</span>
                            <span className="d-block text-secondary">
                              {manager.role}
                            </span>
                          </span>
                        </span>
                      </Label>
                    </div>
                  ))}
                </FormGroup>
              </CardBody>
            </Card>
          </Col>

          <Col md="6" xl="4">
            <Card className="equal-card">
              <CardHeader>
                <h5>Custom Select Group</h5>
              </CardHeader>
              <CardBody>
                <Row>
                  {services.map((service, index) => (
                    <Col xs={12} key={index}>
                      <Card className="shadow-none">
                        <CardBody className="custom-selection address-content">
                          <div className="position-relative">
                            <Label className="check-box">
                              <Input
                                type="radio"
                                name="service"
                                value={service.name}
                                checked={selectedService === service.name}
                                onChange={handleServiceChange}
                              />
                              <span className="radiomark outline-secondary position-absolute"></span>
                              <span className="ms-4 fs-6">{service.name}</span>
                            </Label>
                          </div>
                          <div>
                            <i className={`${service.icon} icon-bg`}></i>
                            <p className="text-muted">{service.description}</p>
                          </div>
                        </CardBody>
                      </Card>
                    </Col>
                  ))}
                </Row>
              </CardBody>
            </Card>
          </Col>
        </Row>
      </Container>
    </div>
  );
};

export default CheckboxRadioPage;
