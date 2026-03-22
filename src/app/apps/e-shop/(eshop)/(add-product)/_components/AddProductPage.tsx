"use client";
import React, { useState } from "react";
import Link from "next/link";
import { Theme } from "react-select";
import {
  Button,
  Card,
  CardBody,
  Col,
  Container,
  Form,
  FormGroup,
  FormText,
  Input,
  Label,
  Modal,
  ModalBody,
  ModalFooter,
  ModalHeader,
  Row,
} from "reactstrap";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import { IconPlus, IconStack2 } from "@tabler/icons-react";
import TextEditor from "@/Component/CommonElements/TextEditor";
import UploadProduct from "@/Component/CommonElements/UploadProduct";
import Select from "react-select";

interface Option {
  value: string;
  label: string;
}

export const options: Option[] = [
  { value: "", label: "Select a category" },
  { value: "IN", label: "Industry" },
  { value: "FN", label: "Functionality" },
  { value: "CN", label: "Customer Needs" },
  { value: "CP", label: "Customer Preferences" },
  { value: "DE", label: "Demographics" },
];

export const options2: Option[] = [
  { value: "Cl", label: "Clothing" },
  { value: "SH", label: "Shoes" },
  { value: "TO", label: "Toys" },
  { value: "IT", label: "Internet Of Things" },
  { value: "BS", label: "Books & Stationaries" },
  { value: "AS", label: "Art Supplies" },
];

const AddProductPage = () => {
  const [hasMounted, setHasMounted] = React.useState(false);
  const [modal, setModal] = useState(false);

  const toggle = () => setModal(!modal);

  React.useEffect(() => {
    setHasMounted(true);
  }, []);

  if (!hasMounted) return null;

  const customTheme = (theme: Theme) => {
    return {
      ...theme,
      colors: {
        ...theme.colors,
        primary: "#241185",
        primary25: "#6a4ff7",
        neutral0: "#fff",
        neutral5: "#f1f1f1",
        neutral10: "#160a60",
      },
    };
  };

  return (
    <div className="container-fluid">
      <Container fluid>
        <Breadcrumbs
          mainTitle="Add Product"
          title="Apps"
          path={["E-shop", "Add Product"]}
          Icon={IconStack2}
        />
        <div className="row">
          <Col lg="8">
            <Card>
              <CardBody>
                <div className="app-product-section">
                  {/* General Information */}
                  <div className="main-title mb-3">
                    <h6>General Information</h6>
                  </div>
                  <Form className="app-form">
                    <FormGroup>
                      <Label for="productTitle">Product Title</Label>
                      <Input type="text" id="productTitle" />
                    </FormGroup>
                    <FormGroup>
                      <Label for="brandName">Brand Name</Label>
                      <Input type="text" id="brandName" />
                    </FormGroup>
                    <FormGroup>
                      <Label>Product Description</Label>
                      <TextEditor />
                    </FormGroup>
                  </Form>

                  <div className="app-divider-v dashed my-4" />

                  {/* Category */}
                  <div className="main-title mb-3">
                    <h6>Category</h6>
                  </div>
                  <Form className="app-form">
                    <Row>
                      <Col md="6">
                        <FormGroup>
                          <Label>Product Category</Label>
                          <Select
                            options={options}
                            isClearable
                            isSearchable
                            theme={customTheme}
                          />
                        </FormGroup>
                      </Col>
                      <Col md="6">
                        <FormGroup>
                          <Label>Product Tags</Label>
                          <Select options={options2} isClearable isSearchable />
                        </FormGroup>
                      </Col>
                    </Row>
                  </Form>

                  <div className="app-divider-v dashed my-4" />

                  {/* Inventory */}
                  <div className="main-title mb-3">
                    <h6>Inventory</h6>
                  </div>
                  <Form className="app-form">
                    <Row>
                      <Col md="3">
                        <FormGroup>
                          <Label>SKU (Optional)</Label>
                          <Input type="text" />
                        </FormGroup>
                      </Col>
                      <Col md="6">
                        <FormGroup>
                          <Label>Barcode</Label>
                          <Input type="text" />
                        </FormGroup>
                      </Col>
                      <Col md="3">
                        <FormGroup>
                          <Label>Quantity</Label>
                          <Input type="text" />
                        </FormGroup>
                      </Col>
                    </Row>
                  </Form>

                  <div className="app-divider-v dashed my-4" />

                  {/* Shipping */}
                  <div className="main-title mb-3">
                    <h6>Shipping</h6>
                  </div>
                  <Form className="app-form">
                    <Row>
                      <Col md="6">
                        <FormGroup>
                          <Label>Weight</Label>
                          <Input type="text" />
                          <small className="form-text text-muted">
                            Package Size (The package you use to ship your
                            product)
                          </small>
                        </FormGroup>
                      </Col>
                      <Col md="6">
                        <FormGroup>
                          <Label>Shipping Days</Label>
                          <Input type="text" />
                        </FormGroup>
                      </Col>
                      <Col md="4">
                        <FormGroup>
                          <Label>Length</Label>
                          <Input type="text" />
                        </FormGroup>
                      </Col>
                      <Col md="4">
                        <FormGroup>
                          <Label>Breadth</Label>
                          <Input type="text" />
                        </FormGroup>
                      </Col>
                      <Col md="4">
                        <FormGroup>
                          <Label>Width</Label>
                          <Input type="text" />
                        </FormGroup>
                      </Col>
                    </Row>

                    {/* Buttons */}
                    <div className="mt-4 text-end">
                      <Button
                        type="button"
                        color="light"
                        className="rounded me-2"
                      >
                        Discard
                      </Button>
                      <Link
                        href="/apps/e-shop/product-details"
                        passHref
                        legacyBehavior
                      >
                        <Button color="primary" className="rounded">
                          Add Product
                        </Button>
                      </Link>
                    </div>
                  </Form>
                </div>
              </CardBody>
            </Card>
          </Col>
          <Col lg="4">
            <Card>
              <CardBody>
                <div className="app-product-section">
                  {/* Product Media */}
                  <div className="main-title">
                    <h6>Product Media</h6>
                  </div>
                  <UploadProduct />

                  <div className="app-divider-v dashed" />

                  {/* Pricing */}
                  <div className="main-title">
                    <h6>Pricing</h6>
                  </div>
                  <Form className="app-form">
                    <Row>
                      <Col xs="12">
                        <FormGroup>
                          <Label>Price</Label>
                          <div className="input-group mb-3">
                            <span className="input-group-text b-r-left">$</span>
                            <Input type="text" className="b-r-right" />
                          </div>
                        </FormGroup>
                      </Col>
                      <Col xs="12">
                        <FormGroup>
                          <Label>Compare at price</Label>
                          <div className="input-group mb-3">
                            <span className="input-group-text b-r-left">$</span>
                            <Input type="text" className="b-r-right" />
                          </div>
                        </FormGroup>
                      </Col>
                      <Col xs="12">
                        <FormGroup>
                          <Label>Discount(%)</Label>
                          <Input type="text" />
                        </FormGroup>
                      </Col>
                      <Col xs="12">
                        <FormGroup>
                          <Label>Discount Type</Label>
                          <Input type="text" />
                        </FormGroup>
                      </Col>
                    </Row>
                  </Form>

                  <div className="app-divider-v dashed" />

                  {/* Product Variants */}
                  <div className="main-title">
                    <h6>Product Variants</h6>
                  </div>
                  <div className="variants-box">
                    <Link href="#" onClick={toggle} className="text-primary">
                      <IconPlus size={18} /> Add Variants
                    </Link>
                  </div>

                  <Modal isOpen={modal} toggle={toggle}>
                    <ModalHeader toggle={toggle}>Modal title</ModalHeader>
                    <ModalBody>
                      <Form className="app-form">
                        <FormGroup>
                          <Label>City</Label>
                          <Input type="select" defaultValue="1">
                            <option>select Option</option>
                            <option value="1">Size</option>
                            <option value="2">Weight</option>
                            <option value="3">Color</option>
                          </Input>
                        </FormGroup>
                        <FormGroup>
                          <Label>Product Height</Label>
                          <Input type="number" placeholder="Enter Number" />
                        </FormGroup>
                      </Form>
                    </ModalBody>
                    <ModalFooter>
                      <Button color="secondary" onClick={toggle}>
                        Close
                      </Button>
                      <Button color="primary" onClick={toggle}>
                        Add Variants
                      </Button>
                    </ModalFooter>
                  </Modal>

                  <div className="app-divider-v dashed" />

                  {/* Visibility */}
                  <div className="main-title">
                    <h6>Visibility</h6>
                  </div>
                  <Form className="mt-2">
                    <FormGroup check className="d-flex align-items-center mt-2">
                      <Input
                        type="radio"
                        name="flexRadioDefault"
                        id="Visibility_1"
                        className="f-s-18 mb-1 m-1"
                      />
                      <Label check htmlFor="Visibility_1">
                        Published
                      </Label>
                    </FormGroup>
                    <FormGroup check className="d-flex align-items-center mt-2">
                      <Input
                        type="radio"
                        name="flexRadioDefault"
                        id="Visibility_2"
                        className="f-s-18 mb-1 m-1"
                      />
                      <Label check htmlFor="Visibility_2">
                        Schedule
                      </Label>
                    </FormGroup>
                    <FormGroup check className="d-flex align-items-center mt-2">
                      <Input
                        type="radio"
                        name="flexRadioDefault"
                        id="Visibility_3"
                        className="f-s-18 mb-1 m-1"
                      />
                      <Label check htmlFor="Visibility_3">
                        Hidden
                      </Label>
                    </FormGroup>
                  </Form>

                  <Form className="app-form mt-4">
                    <FormGroup>
                      <Label>Publish Date</Label>
                      <Input type="text" />
                      <FormText>
                        The category will not be visible until the specified
                        date.
                      </FormText>
                    </FormGroup>
                  </Form>
                </div>
              </CardBody>
            </Card>
          </Col>
        </div>
      </Container>
    </div>
  );
};

export default AddProductPage;
