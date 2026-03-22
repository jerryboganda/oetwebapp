import React, { useState } from "react";
import { Card, CardBody, CardHeader, Col, Row } from "reactstrap";
import { IconMail, IconStarFilled } from "@tabler/icons-react";
import {
  contactsData,
  contactsList,
  peopleList,
} from "@/Data/UiKit/ListData/listPageData";

const CustomLists = () => {
  const [starredContacts, setStarredContacts] = useState<string[]>([]);

  const toggleStar = (id: string) => {
    setStarredContacts((prev: string[]) =>
      prev.includes(id)
        ? prev.filter((contactId) => contactId !== id)
        : [...prev, id]
    );
  };

  return (
    <>
      <Col md={6} xxl={4}>
        <Card>
          <CardHeader>
            <h5>Contacts</h5>
          </CardHeader>
          <CardBody>
            <ul className="list-group b-r-0 list-contact-box">
              {contactsList.map((contact) => (
                <li key={contact.id} className="list-group-item">
                  <div className="d-flex">
                    <div className="me-3">
                      <div className="d-flex align-items-center">
                        <input
                          className="form-check-input me-3"
                          type="checkbox"
                          id={`listcheck${contact.id}`}
                          defaultChecked={contact.checked}
                        />
                        <div
                          className={`w-40 d-flex-center b-r-50 position-relative ${contact.bgColor} b-1-secondary`}
                        >
                          <img
                            src={contact.avatar}
                            alt={contact.name}
                            className="w-40 h-40 object-fit-cover img-fluid b-r-50"
                          />
                          <span
                            className={`position-absolute top-0 end-0 p-1 border border-light rounded-circle ${
                              contact.status === "online"
                                ? "bg-success"
                                : "bg-secondary"
                            }`}
                          ></span>
                        </div>
                      </div>
                    </div>
                    <div className="text-truncate me-1">
                      <h6 className="mb-0">{contact.name}</h6>
                      <div className="text-secondary">{contact.message}</div>
                    </div>
                  </div>
                </li>
              ))}
            </ul>
          </CardBody>
        </Card>
      </Col>

      <Col md={6} xxl={4}>
        <Card className="equal-card">
          <CardHeader>
            <h5>People</h5>
          </CardHeader>
          <CardBody>
            <div className="list-group list-group-flush app-scroll overflow-auto list-people">
              {Object.entries(peopleList).map(([key, peoples]) => (
                <div key={key}>
                  {/* Key for the group */}
                  <div className="list-group-header sticky-top bg-white p-l-10 f-w-500 f-s-16">
                    {key}
                  </div>
                  {peoples.map((data) => (
                    <div className="list-group-item" key={data.id}>
                      {/* Use data.id as the key */}
                      <div className="row">
                        <div className="col-auto">
                          <a href="#">
                            <div className="h-40 w-40 d-flex-center b-r-10 overflow-hidden bg-light-secondary b-1-secondary">
                              <img
                                src={data.image}
                                alt={data.name}
                                className="img-fluid"
                              />
                            </div>
                          </a>
                        </div>
                        <div className="col text-truncate">
                          <a href="#" className="text-dark f-w-600 d-block">
                            {data.name}
                          </a>
                          <div className="text-secondary text-truncate">
                            {data.description}
                          </div>
                        </div>
                        <div className="col-1 icon">
                          <IconMail size={22} className="text-primary me-1" />
                        </div>
                      </div>
                    </div>
                  ))}
                </div>
              ))}
            </div>
          </CardBody>
        </Card>
      </Col>

      <Col md={6} xxl={4}>
        <Card className="overflow-hidden">
          <CardHeader>
            <h5>Contacts List</h5>
          </CardHeader>
          <CardBody className="bg-secondary-300 ">
            <Row className="contact-list">
              {contactsData.map((contact) => (
                <Col md={6} key={contact.id}>
                  <div
                    className="contact-listbox mb-3"
                    onClick={() => toggleStar(contact.id)}
                  >
                    <div className="text-center">
                      <img
                        src={contact.image}
                        alt={contact.name}
                        className={`w-40 h-40 object-fit-cover img-fluid ${contact.bgClass} b-1-secondary b-r-50`}
                      />
                    </div>
                    <div className="contact-stared">
                      <IconStarFilled
                        size={15}
                        className={`me-1 ${
                          starredContacts.includes(contact.id)
                            ? "text-warning"
                            : "text-secondary"
                        }`}
                      />
                    </div>
                    <h6 className="mb-0 mt-2">{contact.name}</h6>
                    <p>{contact.phone}</p>
                    <p>{contact.location}</p>
                  </div>
                </Col>
              ))}
            </Row>
          </CardBody>
        </Card>
      </Col>
    </>
  );
};

export default CustomLists;
