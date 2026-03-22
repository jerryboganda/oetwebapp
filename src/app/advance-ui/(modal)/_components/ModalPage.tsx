"use client";
import React, { useState } from "react";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import {
  Button,
  Card,
  CardBody,
  CardHeader,
  Col,
  Container,
  Row,
} from "reactstrap";
import { Modal, ModalHeader, ModalBody, ModalFooter } from "reactstrap";

import {
  IconArrowBigRight,
  IconArrowBigRightLines,
  IconArrowRight,
  IconBriefcase,
  IconCaretRight,
  IconChevronRight,
} from "@tabler/icons-react";

const ModalPage: React.FC = () => {
  const [modalOpen, setModalOpen] = useState(false);
  const [largeModalOpen, setLargeModalOpen] = useState(false);
  const [extralargeModalOpen, setExtraLargeModalOpen] = useState(false);
  const [centerModalOpen, setCenterModalOpen] = useState(false);
  const [scrollModalOpen, setScrollModalOpen] = useState(false);
  const [fullscreenModalOpen, setFullscreenModalOpen] = useState(false);
  const [fullscreenSmModalOpen, setFullscreenSmModalOpen] = useState(false);
  const [fullscreenLgModalOpen, setFullscreenLgModalOpen] = useState(false);
  const [fullscreenXlModalOpen, setFullscreenXlModalOpen] = useState(false);
  const [fullscreenXxlModalOpen, setFullscreenXxlModalOpen] = useState(false);
  const [primaryModalOpen, setPrimaryModalOpen] = useState(false);
  const [modalVariant, setModalVariant] = useState<
    "primary" | "success" | "warning" | "danger" | "secondary" | "info" | "dark"
  >("primary");
  const toggle = () => setModalOpen(!modalOpen);

  return (
    <>
      <Container fluid>
        <Breadcrumbs
          mainTitle="Modals"
          title="Advance Ui"
          path={["Modals"]}
          Icon={IconBriefcase}
        />
        <Row>
          <Col sm="12" md="6">
            <Card className="equal-card">
              <CardHeader>
                <h5>Default Modal</h5>
                <p className="mb-0 text-secondary">
                  If you want to keep the default modal, you can keep it using{" "}
                  <span className="text-danger">modal-dialog</span>
                </p>
              </CardHeader>
              <CardBody>
                <Button
                  color="light-primary"
                  onClick={toggle}
                  className="rounded"
                >
                  Default Modal
                </Button>
              </CardBody>
            </Card>
          </Col>
          <Col sm="12" md="6">
            <Card>
              <CardHeader>
                <h5>Small Modal</h5>
                <p className="mb-0 text-secondary">
                  if you want to keep the default modal then you can keep it
                  using{" "}
                  <span className="text-danger">
                    modal-dialog or app_modal_sm
                  </span>
                </p>
              </CardHeader>
              <div className="card-body modal-btn">
                <Button
                  color="light-primary"
                  onClick={toggle}
                  className="rounded"
                >
                  Small Modal
                </Button>
                <Button
                  color="light-secondary"
                  onClick={() => setLargeModalOpen(true)}
                  className="rounded"
                >
                  Open Large Modal
                </Button>
                <Button
                  color="light-secondary"
                  onClick={() => setExtraLargeModalOpen(true)}
                  className="rounded"
                >
                  Open Large Modal
                </Button>
              </div>
            </Card>
          </Col>
          <Col sm="12" md="6">
            <Card className="equal-card">
              <CardHeader>
                <h5>Center Modal</h5>
                <p className="mb-0 text-secondary">
                  if you want to keep the default modal then you can keep it
                  using{" "}
                  <span className="text-danger">modal-dialog-centered</span>
                </p>
              </CardHeader>
              <CardBody className="card-body">
                <Button
                  color="light-primary"
                  onClick={() => setCenterModalOpen(true)}
                  className="rounded"
                >
                  Center Modal
                </Button>
              </CardBody>
            </Card>
          </Col>
          <Col sm="12" md="6">
            <Card>
              <CardHeader>
                <h5>Scrollable Modal</h5>
                <p className="mb-0 text-secondary">
                  if you want to keep the default modal then you can keep it
                  using{" "}
                  <span className="text-danger">
                    modal-dialog-centered or modal-dialog-scrollable
                  </span>
                </p>
              </CardHeader>
              <CardBody className="card-body">
                <Button
                  color="light-info"
                  onClick={() => setScrollModalOpen(true)}
                  className="rounded"
                >
                  Open Scrollable Modal
                </Button>
              </CardBody>
            </Card>
          </Col>
          <Col sm="12" md="6">
            <Card className="equal-card">
              <CardHeader>
                <h5>Full Screen Modal</h5>
                <p className="mb-0 text-secondary">
                  if you want to keep the default modal then you can keep it
                  using <span className="text-danger">modal-fullscreen</span>
                </p>
              </CardHeader>
              <CardBody className="card-body">
                <Button
                  color="light-dark"
                  onClick={() => setFullscreenModalOpen(true)}
                  className="rounded"
                >
                  Fullscreen Modal
                </Button>
              </CardBody>
            </Card>
          </Col>
          <Col sm="12" md="6">
            <Card>
              <CardHeader>
                <h5>Full Screen Sm Down Modal</h5>
                <p className="mb-0 text-secondary">
                  if you want to keep the default modal then you can keep it
                  using{" "}
                  <span className="text-danger"> modal-fullscreen-sm-down</span>
                </p>
              </CardHeader>
              <CardBody className="card-body">
                <Button
                  color="light-secondary"
                  onClick={() => setFullscreenSmModalOpen(true)}
                  className="rounded"
                >
                  Fullscreen Below SM Modal
                </Button>
              </CardBody>
            </Card>
          </Col>
          <Col sm="12" md="6">
            <Card>
              <CardHeader>
                <h5>Full-Screen Md Down Modal</h5>
                <p className="mb-0 text-secondary">
                  if you want to keep the default modal then you can keep it
                  using{" "}
                  <span className="text-danger">modal-fullscreen-md-down</span>
                </p>
              </CardHeader>
              <CardBody className="card-body">
                <Button
                  color="light-success"
                  onClick={() => setFullscreenSmModalOpen(true)}
                  className="rounded"
                >
                  Fullscreen Below SM Modal
                </Button>
              </CardBody>
            </Card>
          </Col>
          <Col sm="12" md="6">
            <Card className="equal-card">
              <CardHeader>
                <h5>Full Screen Lg Down Modal</h5>
                <p className="mb-0 text-secondary">
                  if you want to keep the default modal then you can keep it
                  using{" "}
                  <span className="text-danger">modal-fullscreen-lg-down</span>
                </p>
              </CardHeader>
              <CardBody className="card-body">
                <Button
                  color="light-danger"
                  onClick={() => setFullscreenLgModalOpen(true)}
                  className="rounded"
                >
                  Fullscreen Below LG Modal
                </Button>
              </CardBody>
            </Card>
          </Col>
          <Col sm="12" md="6">
            <Card className="equal-card">
              <CardHeader>
                <h5>Full Screen Xl Down Modal</h5>
                <p className="mb-0 text-secondary">
                  if you want to keep the default modal then you can keep it
                  using{" "}
                  <span className="text-danger">modal-fullscreen-Xl-down</span>
                </p>
              </CardHeader>
              <CardBody className="card-body">
                <Button
                  color="light-info"
                  onClick={() => setFullscreenXlModalOpen(true)}
                  className="rounded"
                >
                  Fullscreen Below XL Modal
                </Button>
              </CardBody>
            </Card>
          </Col>
          <Col sm="12" md="6">
            <Card>
              <CardHeader>
                <h5>Full Screen Xxl Down Modal</h5>
                <p className="mb-0 text-secondary">
                  if you want to keep the default modal then you can keep it
                  using{" "}
                  <span className="text-danger">modal-fullscreen-Xxl-down</span>
                </p>
              </CardHeader>
              <CardBody className="card-body">
                <Button
                  color="light-warning"
                  onClick={() => setFullscreenXxlModalOpen(true)}
                  className="rounded"
                >
                  Open Fullscreen Below XXL Modal
                </Button>
              </CardBody>
            </Card>
          </Col>
          <Col xs={12}>
            <Card>
              <CardHeader>
                <h4 className="">Themes Modal</h4>
                <p>You can use custom modals with themes colors.</p>
              </CardHeader>
              <CardBody className="card-body">
                <Button
                  color="outline-primary"
                  className="rounded"
                  onClick={() => {
                    setModalVariant("primary");
                    setPrimaryModalOpen(true);
                  }}
                >
                  Primary
                </Button>{" "}
                <Button
                  color="outline-secondary"
                  className="rounded"
                  onClick={() => {
                    setModalVariant("secondary");
                    setPrimaryModalOpen(true);
                  }}
                >
                  Secondary
                </Button>{" "}
                <Button
                  color="outline-success"
                  className="rounded"
                  onClick={() => {
                    setModalVariant("success");
                    setPrimaryModalOpen(true);
                  }}
                >
                  success
                </Button>{" "}
                <Button
                  color="outline-warning"
                  className="rounded"
                  onClick={() => {
                    setModalVariant("warning");
                    setPrimaryModalOpen(true);
                  }}
                >
                  warning
                </Button>{" "}
                <Button
                  color="outline-info"
                  className="rounded"
                  onClick={() => {
                    setModalVariant("info");
                    setPrimaryModalOpen(true);
                  }}
                >
                  info
                </Button>{" "}
                <Button
                  color="outline-danger"
                  className="rounded"
                  onClick={() => {
                    setModalVariant("danger");
                    setPrimaryModalOpen(true);
                  }}
                >
                  danger
                </Button>{" "}
                <Button
                  color="outline-dark"
                  className="rounded"
                  onClick={() => {
                    setModalVariant("dark");
                    setPrimaryModalOpen(true);
                  }}
                >
                  dark
                </Button>
                {/*box-1-start*/}
                <Modal
                  isOpen={primaryModalOpen}
                  toggle={() => setPrimaryModalOpen(false)}
                  centered
                >
                  <div className="modal-content overflow-hidden">
                    {/* Dynamic Header */}
                    <ModalHeader
                      toggle={() => setPrimaryModalOpen(false)}
                      className={`bg-${modalVariant} d-flex justify-content-between text-white`}
                    >
                      {modalVariant.charAt(0).toUpperCase() +
                        modalVariant.slice(1)}{" "}
                      Modal
                      <Button
                        type="button"
                        onClick={() => setPrimaryModalOpen(false)}
                        className="btn-close m-0 fs-5 bg-none border-0"
                        aria-label="Close"
                      />
                    </ModalHeader>
                    <ModalBody>
                      <h5 className={`mt-0 text-${modalVariant}`}>
                        Quos modi tempora illo fuga blanditiis voluptatum atque.
                      </h5>
                    </ModalBody>
                    <ModalFooter>
                      <Button
                        type="button"
                        color={modalVariant}
                        className="badge text-light-primary fs-6"
                      >
                        Save changes
                      </Button>
                      <Button
                        type="button"
                        color="light-secondary"
                        onClick={() => setPrimaryModalOpen(false)}
                      >
                        Close
                      </Button>
                    </ModalFooter>
                  </div>
                </Modal>
                {/*box-1-end*/}
              </CardBody>
            </Card>
          </Col>
        </Row>

        {/* Small Modal Start */}
        <Modal isOpen={modalOpen} toggle={toggle} size="app_modal_sm">
          <ModalHeader toggle={toggle} className="modal-title text-primary">
            Small Modal
          </ModalHeader>

          <ModalBody className="text-center">
            <div className="d-flex gap-2">
              <img
                src="/images/modals/06.jpg"
                alt="Content marketing"
                width={90}
                height={90}
                className="rounded-pill object-fit-cover h-90 w-90"
              />
              <div className="text-start d-flex flex-column gap-2">
                <h5>Content marketing</h5>
                <p className="m-0">
                  Lorem ipsum dolor sit amet, consectetur adipisicing elit.
                </p>
              </div>
            </div>
          </ModalBody>
          <ModalFooter>
            <Button color="light-secondary" onClick={toggle}>
              Close
            </Button>
            <Button color="light-primary">Save Changes</Button>
          </ModalFooter>
        </Modal>
        {/* Large Modal */}
        <Modal
          isOpen={largeModalOpen}
          toggle={() => setLargeModalOpen(!largeModalOpen)}
          size="app_modal_lg"
        >
          <ModalHeader toggle={() => setLargeModalOpen(false)}>
            Large Modal
          </ModalHeader>
          <ModalBody>
            <div className="row">
              <div className="col-lg-4 text-center">
                <img
                  src="/images/modals/05.png"
                  alt="Large Modal"
                  className="img-fluid"
                />
              </div>
              <div className="col-lg-8 align-self-center">
                <div className="error-content text-center">
                  <h4 className="mb-3">DO NOT ENTER</h4>
                  <button type="button" className="btn btn-light-primary">
                    Back to Dashboard
                  </button>
                </div>
              </div>
            </div>
          </ModalBody>
          <ModalFooter>
            <Button color="secondary" onClick={() => setLargeModalOpen(false)}>
              Close
            </Button>
          </ModalFooter>
        </Modal>

        {/* Extra Large Modal */}
        <Modal
          isOpen={extralargeModalOpen}
          toggle={() => setExtraLargeModalOpen(false)}
          size="app_modal_lg"
          className="app_modal_xl"
        >
          <div className="modal-content overflow-hidden">
            <ModalHeader
              toggle={() => setExtraLargeModalOpen(false)}
              className="bg-primary-800 d-flex justify-content-between"
            >
              Extra Large Modal
            </ModalHeader>
            <ModalBody>
              <p>
                In a professional context it often happens that private or
                corporate clients order a publication to be made and presented
                with the actual content still not being ready. Think of a news
                blog that&#39;s filled with content hourly on the day of going
                live.
              </p>
            </ModalBody>
            <ModalFooter>
              <Button color="light-primary">Save changes</Button>
              <Button
                color="light-secondary"
                onClick={() => setExtraLargeModalOpen(false)}
              >
                Close
              </Button>
            </ModalFooter>
          </div>
        </Modal>

        {/*center modal start */}
        <Modal
          isOpen={centerModalOpen}
          toggle={() => setCenterModalOpen(false)}
          centered
          className="modal-dialog-centered"
        >
          <div className="modal-content overflow-hidden">
            <ModalHeader
              toggle={() => setCenterModalOpen(false)}
              className="d-flex justify-content-between modal-title mb-0"
            >
              Center Modal
              <Button
                type="button"
                onClick={() => setCenterModalOpen(false)}
                className="btn-close m-0 fs-5 bg-none border-0"
                aria-label="Close"
              />
            </ModalHeader>
            <ModalBody>
              <div className="row">
                <div className="col-lg-3 text-center align-self-center">
                  <img
                    src="/images/modals/04.png"
                    alt="Web designer"
                    className="img-fluid b-r-10"
                  />
                </div>
                <div className="col-lg-9 ps-4">
                  <h5>Web designer</h5>
                  <ul className="mt-3 mb-0 list-disc">
                    <li>
                      Lorem, ipsum dolor sit amet consectetur adipisicing elit.
                    </li>
                  </ul>
                </div>
              </div>
            </ModalBody>

            {/* Modal Footer */}
            <ModalFooter>
              <Button color="light-primary">Save changes</Button>
              <Button
                color="light-secondary"
                onClick={() => setCenterModalOpen(false)}
              >
                Close
              </Button>
            </ModalFooter>
          </div>
        </Modal>

        {/*scrollbar model start */}
        <Modal
          isOpen={scrollModalOpen}
          toggle={() => setScrollModalOpen(false)}
          scrollable
          centered
        >
          <div className="modal-content">
            <ModalHeader
              toggle={() => setScrollModalOpen(false)}
              className="d-flex justify-content-between"
            >
              Scroll Modal
              <Button
                type="button"
                onClick={() => setScrollModalOpen(false)}
                className="btn-close m-0 fs-5 bg-none border-0"
                aria-label="Close"
              />
            </ModalHeader>

            {/* Modal Body */}
            <ModalBody className="h-90">
              <p>
                <IconChevronRight
                  size={22}
                  className="text-secondary fw-semibold"
                />{" "}
                However, reviewers tend to be distracted by comprehensible
                content, say, a random text copied from a newspaper or the
                internet. They are likely to focus on the text, disregarding the
                layout and its elements.
              </p>

              <p>
                <IconChevronRight
                  size={22}
                  className="text-secondary fw-semibold"
                />{" "}
                It was found by Richard McClintock, a philologist, director of
                publications at Hampden-Sydney College in Virginia; he searched
                for citings of <em>consectetur</em> in classical Latin
                literature, a term of remarkably low frequency in that literary
                corpus.
              </p>
            </ModalBody>

            {/* Modal Footer */}
            <ModalFooter>
              <Button color="light-primary">Save changes</Button>
              <Button
                color="light-secondary"
                onClick={() => setScrollModalOpen(false)}
              >
                Close
              </Button>
            </ModalFooter>
          </div>
        </Modal>

        {/*full screen modal start */}
        <Modal
          isOpen={fullscreenModalOpen}
          toggle={() => setFullscreenModalOpen(false)}
          fullscreen
          scrollable
        >
          <div className="modal-content">
            <ModalHeader
              toggle={() => setFullscreenModalOpen(false)}
              className="d-flex justify-content-between"
            >
              Full screen modal
              <Button
                type="button"
                onClick={() => setFullscreenModalOpen(false)}
                className="btn-close m-0 fs-5 bg-none border-0"
                aria-label="Close"
              />
            </ModalHeader>
            <ModalBody>
              <p>
                <IconChevronRight
                  size={22}
                  className="text-secondary fw-semibold"
                />{" "}
                I must explain to you how all this mistaken idea of denouncing
                pleasure and praising pain was born, and I will give you a
                complete account of the system...
              </p>
              <p>
                <IconChevronRight
                  size={22}
                  className="text-secondary fw-semibold"
                />{" "}
                I must explain to you how all this mistaken idea of denouncing
                pleasure and praising pain was born, and I will give you a
                complete account of the system...
              </p>
              <p>
                <IconChevronRight
                  size={22}
                  className="text-secondary fw-semibold"
                />{" "}
                I must explain to you how all this mistaken idea of denouncing
                pleasure and praising pain was born, and I will give you a
                complete account of the system...
              </p>
              <p>
                <IconChevronRight
                  size={22}
                  className="text-secondary fw-semibold"
                />{" "}
                I must explain to you how all this mistaken idea of denouncing
                pleasure and praising pain was born, and I will give you a
                complete account of the system...
              </p>
            </ModalBody>

            {/* Footer */}
            <ModalFooter>
              <Button
                color="light-secondary"
                onClick={() => setFullscreenModalOpen(false)}
              >
                Close
              </Button>
              <Button color="light-primary">Save changes</Button>
            </ModalFooter>
          </div>
        </Modal>

        {/*Full-screen-sm-down modal start */}
        <Modal
          isOpen={fullscreenSmModalOpen}
          toggle={() => setFullscreenSmModalOpen(false)}
          className="modal-fullscreen-sm-down"
        >
          <div className="modal-content">
            <ModalHeader
              toggle={() => setFullscreenSmModalOpen(false)}
              className="d-flex justify-content-between"
            >
              Full screen below sm
              <Button
                type="button"
                onClick={() => setFullscreenSmModalOpen(false)}
                className="btn-close m-0 fs-5 bg-none border-0"
                aria-label="Close"
              />
            </ModalHeader>
            <ModalBody>
              <p>
                <IconCaretRight
                  size={22}
                  className="text-secondary fw-semibold"
                />{" "}
                I must explain to you how all this mistaken idea of denouncing
                pleasure and praising pain was born and I will give you a
                complete account of the system...
              </p>
              <p>
                <IconCaretRight
                  size={22}
                  className="text-secondary fw-semibold"
                />{" "}
                I must explain to you how all this mistaken idea of denouncing
                pleasure and praising pain was born and I will give you a
                complete account of the system...
              </p>
            </ModalBody>

            {/* Footer */}
            <ModalFooter>
              <Button
                color="light-secondary"
                size="sm"
                onClick={() => setFullscreenSmModalOpen(false)}
              >
                Close
              </Button>
            </ModalFooter>
          </div>
        </Modal>

        {/*Full-screen-lg-down modal start  */}
        <Modal
          isOpen={fullscreenLgModalOpen}
          toggle={() => setFullscreenLgModalOpen(false)}
          className="modal-fullscreen-lg-down"
        >
          <div className="modal-content">
            <ModalHeader
              toggle={() => setFullscreenLgModalOpen(false)}
              className="d-flex justify-content-between"
            >
              Full screen below lg
              <Button
                type="button"
                onClick={() => setFullscreenLgModalOpen(false)}
                className="btn-close m-0 fs-5 bg-none border-0"
                aria-label="Close"
              />
            </ModalHeader>
            <ModalBody>
              <p>
                <IconArrowRight
                  size={22}
                  className="text-secondary fw-semibold"
                />{" "}
                I must explain to you how all this mistaken idea of denouncing
                pleasure and praising pain was born and I will give you a
                complete account of the system...
              </p>
              <p>
                <IconArrowRight
                  size={22}
                  className="text-secondary fw-semibold"
                />{" "}
                I must explain to you how all this mistaken idea of denouncing
                pleasure and praising pain was born and I will give you a
                complete account of the system...
              </p>
              <p>
                <IconArrowRight
                  size={22}
                  className="text-secondary fw-semibold"
                />{" "}
                I must explain to you how all this mistaken idea of denouncing
                pleasure and praising pain was born and I will give you a
                complete account of the system...
              </p>
            </ModalBody>

            {/* Footer */}
            <ModalFooter>
              <Button
                color="light-secondary"
                size="sm"
                onClick={() => setFullscreenLgModalOpen(false)}
              >
                Close
              </Button>
            </ModalFooter>
          </div>
        </Modal>
        {/*Full-screen-lg-down modal end */}

        {/*Full-screen-xl-down modal start  */}
        <Modal
          isOpen={fullscreenXlModalOpen}
          toggle={() => setFullscreenXlModalOpen(false)}
          className="modal-fullscreen-xl-down"
        >
          <div className="modal-content">
            <ModalHeader
              toggle={() => setFullscreenXlModalOpen(false)}
              className="d-flex justify-content-between"
            >
              Full screen below xl
              <Button
                type="button"
                onClick={() => setFullscreenXlModalOpen(false)}
                className="btn-close m-0 fs-5 bg-none border-0"
                aria-label="Close"
              />
            </ModalHeader>
            <ModalBody>
              <p>
                <IconArrowBigRight
                  size={22}
                  className="text-secondary fw-semibold"
                />{" "}
                I must explain to you how all this mistaken idea of denouncing
                pleasure and praising pain was born and I will give you a
                complete account of the system, and expound the actual teachings
                of the great explorer of the truth, the master-builder of human
                happiness.
              </p>
            </ModalBody>

            {/* Footer */}
            <ModalFooter>
              <Button
                color="light-secondary"
                size="sm"
                onClick={() => setFullscreenXlModalOpen(false)}
              >
                Close
              </Button>
            </ModalFooter>
          </div>
        </Modal>
        {/*Full-screen-xl-down modal end */}

        {/*Full-screen-xxl-down modal start */}
        <Modal
          isOpen={fullscreenXxlModalOpen}
          toggle={() => setFullscreenXxlModalOpen(false)}
          className="modal-fullscreen-xxl-down"
        >
          <div className="modal-content">
            <ModalHeader
              toggle={() => setFullscreenXxlModalOpen(false)}
              className="d-flex justify-content-between"
            >
              Full screen below xxl
              <Button
                type="button"
                onClick={() => setFullscreenXxlModalOpen(false)}
                className="btn-close m-0 fs-5 bg-none border-0"
                aria-label="Close"
              />
            </ModalHeader>
            <ModalBody>
              <p>
                <IconArrowBigRightLines
                  size={22}
                  className="text-secondary fw-semibold"
                />{" "}
                I must explain to you how all this mistaken idea of denouncing
                pleasure and praising pain was born and I will give you a
                complete account of the system, and expound the actual teachings
                of the great explorer of the truth, the master-builder of human
                happiness.
              </p>
            </ModalBody>
            <ModalFooter>
              <Button
                color="light-secondary"
                size="sm"
                onClick={() => setFullscreenXxlModalOpen(false)}
              >
                Close
              </Button>
            </ModalFooter>
          </div>
        </Modal>
        {/*< Full-screen-xxl-down modal end */}
      </Container>
    </>
  );
};

export default ModalPage;
