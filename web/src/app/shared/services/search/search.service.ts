import { Injectable } from '@angular/core';
import { HubConnection, HubConnectionBuilder } from '@microsoft/signalr';
import { Observable, Subject } from 'rxjs';
import { Notification } from '../../models/notification';
import { NotificationType } from '../../models/notification-type';
import { SearchParameters } from '../../models/search-parameters';
import { HttpClient, HttpHeaders } from '@angular/common/http';

@Injectable({
  providedIn: 'root',
})
export class SearchService {
  private connection: HubConnection | undefined;
  notification: Subject<Notification>;
  searchParameters: Subject<SearchParameters | null>;
  private apiUrl: string = 'https://localhost:44373/';
  private duplicatesApiUrl: string = `${this.apiUrl}api/Duplicates/`;
  // private httpHeaders: HttpHeaders = new HttpHeaders({
  //   'Content-Type': 'application/json; charset=utf-8',
  // });

  constructor(private http: HttpClient) {
    this.notification = new Subject();
    this.searchParameters = new Subject();
  }

  startConnection(): Promise<void> {
    const url = `${this.apiUrl}notifications`;
    this.connection = new HubConnectionBuilder().withUrl(url).build();

    return this.connection.start();
  }

  stopConnection() {
    this.connection
      ?.stop()
      .then(() => console.log('Connection closed'))
      .catch((error) => {
        if (error) {
          console.log(`Connection not stopped ${error}`);
        }
      });
  }

  launchSearch(searchParameters: SearchParameters | null): Observable<any> {
    this.sendSearchParameters(searchParameters);
    const url = `${this.duplicatesApiUrl}findDuplicates`;
    return this.http.post<any>(url, searchParameters);
  }

  sendSearchParameters(searchParameters: SearchParameters | null): void {
    this.searchParameters.next(searchParameters);
  }

  addNotificationListener() {
    this.connection!.on('notify', (notification: Notification) => {
      console.log(notification);
      if (notification.type == NotificationType.Exception) {
      } else this.notification.next(notification);
    });
  }
}
