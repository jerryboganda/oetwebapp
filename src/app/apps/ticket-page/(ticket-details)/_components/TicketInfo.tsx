import React from "react";
import { Card, CardBody } from "reactstrap";
import { ticketDetails } from "@/Data/Apps/Ticket/Ticket";
import { IconPhoneCall, IconExternalLink, IconUser } from "@tabler/icons-react";

const TicketInfo = () => {
  return (
    <Card>
      <CardBody>
        <div className="ticket-details-profile">
          <div className="ticket-profile mb-4">
            <div className="h-45 w-45 d-flex-center b-r-50 overflow-hidden text-bg-secondary me-3">
              <img src="/images/avatar/2.png" alt="" className="img-fluid" />
            </div>
            <div>
              <h6 className="mb-0">Marry jones</h6>
              <p className="text-secondary mb-0">(678)456-7890</p>
            </div>
          </div>
          <div className="ticket-profile-con">
            <span>
              {" "}
              <IconPhoneCall size={20} className="text-success" />
            </span>
            <span>
              {" "}
              <IconExternalLink size={20} className="text-danger" />
            </span>
            <span>
              {" "}
              <IconUser size={20} className="text-info" />
            </span>
            <div className="app-divider-v dashed pt-4 pb-4"></div>
          </div>
        </div>

        <div className="about-list pt-0">
          {ticketDetails.map((item, index) => (
            <div key={index} className="d-flex justify-content-between mb-2">
              <span className="f-w-600">{item.label}</span>
              <span className="f-w-500 text-secondary small">{item.value}</span>
            </div>
          ))}
        </div>
      </CardBody>
    </Card>
  );
};

export default TicketInfo;
